using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.ThemeSongs

{


    public class ThemeSongsManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly Timer _timer;
        private readonly ILogger<ThemeSongsManager> _logger;

        public ThemeSongsManager(ILibraryManager libraryManager, ILogger<ThemeSongsManager> logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _timer = new Timer(_ => OnTimerElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private IEnumerable<Series> GetSeriesFromLibrary()
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] {BaseItemKind.Series},
                IsVirtualItem = false,
                Recursive = true,
            }).OfType<Series>();
        }

        private static string? GetConfiguredTemplate()
        {
            var configuredTemplate = Plugin.Instance?.Configuration?.ThemeSongUrlTemplate;
            return string.IsNullOrWhiteSpace(configuredTemplate) ? null : configuredTemplate;
        }

        private bool TryBuildThemeSongUrl(Series series, string template, out string link)
        {
            var result = template;
            var replacements = new List<(string Placeholder, string Value, string Name)>
            {
                ("{tvdbId}", series.GetProviderId(MetadataProvider.Tvdb), "tvdbId"),
                ("{imdbId}", series.GetProviderId(MetadataProvider.Imdb), "imdbId"),
                ("{tmdbId}", series.GetProviderId(MetadataProvider.Tmdb), "tmdbId")
            };

            var missingPlaceholders = new List<string>();

            foreach (var replacement in replacements)
            {
                if (!result.Contains(replacement.Placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(replacement.Value))
                {
                    missingPlaceholders.Add(replacement.Name);
                    continue;
                }

                result = result.Replace(replacement.Placeholder, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }

            if (missingPlaceholders.Count > 0)
            {
                _logger.LogInformation("Skipping theme song download for {seriesName}. Missing provider ids: {missingIds}", series.Name, string.Join(", ", missingPlaceholders));
                link = string.Empty;
                return false;
            }

            link = result;
            return true;
        }


        public void DownloadAllThemeSongs()
        {
            var series = GetSeriesFromLibrary();
            var template = GetConfiguredTemplate();
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogInformation("Skipping theme song download: no URL template configured.");
                return;
            }

            foreach (var serie in series)
            {
                if (!serie.GetThemeSongs().Any())
                {
                    if (!TryBuildThemeSongUrl(serie, template, out var link))
                    {
                        continue;
                    }

                    var themeSongPath = Path.Join(serie.Path, "theme.mp3");
                    _logger.LogDebug("Trying to download {seriesName}, {link}", serie.Name, link);

                    try
                    {
                        using var client = new WebClient();
                        client.DownloadFile(link, themeSongPath);
                        _logger.LogInformation("{seriesName} theme song successfully downloaded from {link}", serie.Name, link);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Unable to download theme song for {seriesName} from {link}", serie.Name, link);
                    }
                }
            }        
        }


        public void NormalizeAllThemeSongsVolume()
        {
            var series = GetSeriesFromLibrary();

            foreach (var serie in series)
            {
                var dir = serie.Path;
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    continue;
                }

                // Find files named like "theme.*" in the series folder
                var files = Directory.GetFiles(dir, "theme.*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    try
                    {
                        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(file));

                        var args = $"-y -i \"{file}\" -filter:a \"volume=0.5\" -vn -c:a libmp3lame -q:a 2 \"{temp}\"";

                        var psi = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = args,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(psi);
                        if (process == null)
                        {
                            _logger.LogWarning("FFmpeg process could not be started for {file}", file);
                            continue;
                        }

                        var stderr = process.StandardError.ReadToEnd();
                        var stdout = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            _logger.LogWarning("FFmpeg failed for {file}: {err}", file, stderr);
                            if (File.Exists(temp)) File.Delete(temp);
                            continue;
                        }

                        File.Copy(temp, file, true);
                        File.Delete(temp);
                        _logger.LogInformation("Normalized volume for {file}", file);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Error normalizing volume for {file}", file);
                    }
                }
            }
        }


        private void OnTimerElapsed()
        {
            // Stop the timer until next update
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public Task RunAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
