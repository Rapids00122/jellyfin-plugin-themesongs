using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeSongs.ScheduledTasks
{
    public class NormalizeThemeSongsVolumeTask : IScheduledTask
    {
        private readonly ILogger<ThemeSongsManager> _logger;
        private readonly ThemeSongsManager _themeSongsManager;

        public NormalizeThemeSongsVolumeTask(ILibraryManager libraryManager, ILogger<ThemeSongsManager> logger)
        {
            _logger = logger;
            _themeSongsManager = new ThemeSongsManager(libraryManager, logger);
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.LogInformation("Starting Normalize Theme Songs Volume task...");
            _themeSongsManager.NormalizeAllThemeSongsVolume();
            _logger.LogInformation("Theme songs volume normalization completed");
            return Task.CompletedTask;
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Execute(cancellationToken, progress);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // No default trigger; leave it manual or configure in Jellyfin
            yield break;
        }

        public string Name => "Normalize Theme Songs Volume";
        public string Key => "NormalizeThemeSongsVolume";
        public string Description => "Normalize theme songs audio volume using ffmpeg (sets volume to 0.5)";
        public string Category => "Theme Songs";
    }
}
