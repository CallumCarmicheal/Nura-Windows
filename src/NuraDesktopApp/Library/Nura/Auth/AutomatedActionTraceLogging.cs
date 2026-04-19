using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class AutomatedActionTraceLogging {
    internal static void LogTrace(string prefix, Dictionary<string, object?>? responseBody, SessionLogger logger) {
        if (responseBody is null) {
            logger.WriteLine($"{prefix}.present=false");
            return;
        }

        var trace = AutomatedActionTraceParser.Parse(responseBody);
        logger.WriteLine($"{prefix}.present={trace.ActionCount > 0}");
        logger.WriteLine($"{prefix}.action_count={trace.ActionCount}");
        logger.WriteLine($"{prefix}.run_count={trace.RunCount}");
        logger.WriteLine($"{prefix}.wait_count={trace.WaitCount}");
        logger.WriteLine($"{prefix}.enhanced_wait_count={trace.EnhancedWaitCount}");
        logger.WriteLine($"{prefix}.call_home_count={trace.CallHomeCount}");
        logger.WriteLine($"{prefix}.unencrypted_run_count={trace.UnencryptedRunCount}");
        logger.WriteLine($"{prefix}.app_encrypted_run_count={trace.AppEncryptedRunCount}");
        logger.WriteLine($"{prefix}.app_trigger_count={trace.AppTriggerCount}");
        logger.WriteLine($"{prefix}.manual_wait_count={trace.ManualWaitCount}");
        logger.WriteLine($"{prefix}.app_encrypted_response_run_count={trace.AppEncryptedResponseRunCount}");

        for (var index = 0; index < trace.CallHomeEndpoints.Count; index++) {
            logger.WriteLine($"{prefix}.call_home.{index}.endpoint={trace.CallHomeEndpoints[index]}");
        }

        for (var index = 0; index < trace.ManualWaitTriggers.Count; index++) {
            logger.WriteLine($"{prefix}.manual_wait.{index}.trigger={trace.ManualWaitTriggers[index]}");
        }

        for (var index = 0; index < trace.AppTriggers.Count; index++) {
            var trigger = trace.AppTriggers[index];
            logger.WriteLine($"{prefix}.app_trigger.{index}.name={trigger.Trigger}");
            logger.WriteLine($"{prefix}.app_trigger.{index}.data.json={trigger.DataJson}");
        }
    }
}
