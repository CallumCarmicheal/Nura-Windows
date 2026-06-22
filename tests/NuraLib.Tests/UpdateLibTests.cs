using UpdateLib;

internal sealed class UpdateLibTests {
    public void RunAll() {
        CurrentVersionText_IsAvailable();
        SkippedUpdateState_RoundTripsAndClears();
    }

    private static void CurrentVersionText_IsAvailable() {
        if (string.IsNullOrWhiteSpace(AutoUpdater.GetCurrentVersionText())) {
            throw new InvalidOperationException("Expected the updater to resolve the current application version.");
        }
    }

    private static void SkippedUpdateState_RoundTripsAndClears() {
        var originalOptions = AutoUpdater.Options;
        var isolatedOptions = new AutoUpdaterOptions {
            ProductName = $"Nura-Windows-Test-{Guid.NewGuid():N}"
        };

        AutoUpdater.Options = isolatedOptions;

        try {
            var update = new UpdateInfo { VersionText = "9.9.9-test" };

            AssertFalse(AutoUpdater.IsUpdateSkipped(update), "A new isolated update state should not be skipped.");
            AutoUpdater.SkipUpdate(update);
            AssertTrue(AutoUpdater.IsUpdateSkipped(update), "Skipped update state should match the candidate version.");

            AutoUpdater.ClearSkippedUpdate();
            AssertFalse(AutoUpdater.IsUpdateSkipped(update), "Clearing the skipped update should resume startup alerts.");
        } finally {
            AutoUpdater.ClearSkippedUpdate();
            AutoUpdater.Options = originalOptions;
        }
    }

    private static void AssertTrue(bool value, string message) {
        if (!value) {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool value, string message) => AssertTrue(!value, message);
}
