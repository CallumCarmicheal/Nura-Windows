namespace NuraDesktopConsole.Actions;

internal static class ActionHandler {
    internal sealed record ArgumentDescriptor(
        string Flag,
        string Name = "",
        bool Optional = true,
        string? Default = null,
        string Description = "",
        string[]? AllowedValues = null);

    internal sealed record ActionDescriptor(
        string[] Commands,
        Func<IAction> Factory,
        string Example,
        string HelpText,
        ArgumentDescriptor[]? Arguments = null);

    internal static readonly IReadOnlyList<ActionDescriptor> Actions =
    [
        new(
            ["probe devices"],
            static () => new ActionProbeDevices(),
            Example: "probe devices",
            HelpText: "List connected Nuraphone devices and select one if multiple are available."),
        new(
            ["probe hw-info"],
            static () => new ActionProbeHardwareInfo(),
            Example: "probe hw-info",
            HelpText: "Connect to the selected Nuraphone and read unencrypted serial and firmware metadata."),
        new(
            ["flow init-to-start3"],
            static () => new ActionFlowInitToStart3(),
            Example: "flow init-to-start3",
            HelpText: "Run the full working bootstrap chain through app-session, auth refresh/login fallback, hardware probe, session-start, and the continuation steps up to session/start_3. If login is needed, prompt in the console for email and code, with .email to resend/change and .quit to abort."),
        new(
            ["protocol plan"],
            static () => new ActionPlan(),
            Example: "protocol plan",
            HelpText: "Print the offline local-control bootstrap plan."),
        new(
            ["protocol respond"],
            static () => new ActionRespond(),
            Example: "protocol respond --challenge-hex <16-byte-hex>",
            HelpText: "Generate the app GMAC response for a headset challenge.",
            Arguments:
            [
                new("--challenge-hex <hex>", "challenge-hex", Optional: false, Description: "16-byte headset challenge as hex")
            ]),
        new(
            ["protocol encrypt"],
            static () => new ActionEncrypt(),
            Example: "protocol encrypt --payload-hex <hex> [--unauth]",
            HelpText: "Wrap a plaintext payload in the Nuraphone authenticated or unauthenticated container.",
            Arguments:
            [
                new("--payload-hex <hex>", "payload-hex", Optional: false, Description: "Plaintext payload bytes"),
                new("--unauth", "unauth", Description: "Use the unauthenticated CTR path instead of authenticated GCM")
            ]),
        new(
            ["protocol parse"],
            static () => new ActionParse(),
            Example: "protocol parse --frame-hex <hex>",
            HelpText: "Parse a raw GAIA response frame.",
            Arguments:
            [
                new("--frame-hex <hex>", "frame-hex", Optional: false, Description: "Full GAIA frame as hex")
            ]),
        new(
            ["extract logcat-auth545 language-change"],
            static () => new ActionExtractLogcatAuth545LanguageChange(),
            Example: "extract logcat-auth545 language-change --log-path <path> [--output-dir <path>]",
            HelpText: "Offline parser for full NURA_FRIDA logcat captures. Extracts change_language Bluetooth TX payloads, writes raw encrypted stage blobs, and attempts app_enc key/nonce decryption without touching the headset or backend.",
            Arguments:
            [
                new("--log-path <path>", "log-path", Optional: false, Description: "Full NURA_FRIDA log containing net454.bt.tx lines and session/start_4 app_enc key/nonce"),
                new("--output-dir <path>", "output-dir", Description: "Optional output directory; defaults to a timestamped logs folder")
            ]),
        new(
            ["extract frida-folder language-probe"],
            static () => new ActionExtractFridaFolderLanguageProbe(),
            Example: "extract frida-folder language-probe --folder <path> [--output-dir <path>]",
            HelpText: "Probe a file-based Frida language-change capture. Replays response packet order with the captured app_enc key/nonce and reports whether authenticated packets verify under likely counter directions.",
            Arguments:
            [
                new("--folder <path>", "folder", Optional: false, Description: "Frida nura_logs run folder containing *.req.json, *.resp.json, and Bluetooth NDJSON files"),
                new("--output-dir <path>", "output-dir", Description: "Optional output directory; defaults to crypto_probe under the Frida run folder")
            ]),
        new(
            ["headset live-handshake"],
            static () => new ActionLiveHandshake(),
            Example: "headset live-handshake",
            HelpText: "Connect to the headset, complete app crypto, and perform safe reads."),
        new(
            ["headset fresh-nonce-test"],
            static () => new ActionFreshNonceTest(),
            Example: "headset fresh-nonce-test",
            HelpText: "Generate a fresh random nonce and verify local control still works."),
        new(
            ["headset dump-metadata"],
            static () => new ActionDumpMetadata(),
            Example: "headset dump-metadata",
            HelpText: "Read a wider set of local authenticated metadata from the headset."),
        new(
            ["headset anc-toggle-test"],
            static () => new ActionAncToggleTest(),
            Example: "headset anc-toggle-test",
            HelpText: "Read ANC state, toggle it, wait, and restore the original value."),
        new(
            ["headset run-raw"],
            static () => new ActionRunRaw(),
            Example: "headset run-raw --packet-hex <hex> [--read-timeout-ms <int>] [--gaia-version <int>] [--gaia-flags <hex>]",
            HelpText: "Connect to the headset, send a raw GAIA packet, and log the first response.",
            Arguments:
            [
                new("--packet-hex <hex>", "packet-hex", Optional: false, Description: "Either a full GAIA frame or raw vendor+command+payload bytes"),
                new("--read-timeout-ms <int>", "read-timeout-ms", Default: "5000", Description: "Timeout for the first response frame"),
                new("--gaia-version <int>", "gaia-version", Default: "1", Description: "GAIA version to use when packet-hex is raw vendor+command+payload"),
                new("--gaia-flags <hex>", "gaia-flags", Default: "0x00", Description: "GAIA flags byte to use when packet-hex is raw vendor+command+payload")
            ]),
        new(
            ["headset bootstrap-step"],
            static () => new ActionBootstrapStep(),
            Example: "headset bootstrap-step --packet-hex <hex> [--read-timeout-ms <int>] [--gaia-version <int>] [--gaia-flags <hex>]",
            HelpText: "Run a backend-provided bootstrap packet against the headset and log the first response.",
            Arguments:
            [
                new("--packet-hex <hex>", "packet-hex", Optional: false, Description: "Bootstrap packet bytes from session/start, usually vendor+command+payload"),
                new("--read-timeout-ms <int>", "read-timeout-ms", Default: "5000", Description: "Timeout for the first response frame"),
                new("--gaia-version <int>", "gaia-version", Default: "1", Description: "GAIA version to use when packet-hex is raw vendor+command+payload"),
                new("--gaia-flags <hex>", "gaia-flags", Default: "0x00", Description: "GAIA flags byte to use when packet-hex is raw vendor+command+payload")
            ]),
        new(
            ["headset probe-server-entry"],
            static () => new ActionProbeServerEntry(),
            Example: "headset probe-server-entry [--payload-hex <hex>] [--command-raw-list <csv>] [--read-timeout-ms <int>] [--gaia-version <int>] [--gaia-flags <hex>]",
            HelpText: "Probe alternate EntryServerEncrypted command families for a saved session/start run-packet payload.",
            Arguments:
            [
                new("--payload-hex <hex>", "payload-hex", Description: "Optional explicit run-packet payload; otherwise uses the first saved run packet"),
                new("--command-raw-list <csv>", "command-raw-list", Default: "0x0010,0x001e,0x0012,0x0020", Description: "Comma-separated raw command ids to try on fresh reconnects"),
                new("--read-timeout-ms <int>", "read-timeout-ms", Default: "5000", Description: "Timeout for each headset response frame"),
                new("--gaia-version <int>", "gaia-version", Default: "1", Description: "GAIA version to use for the probe packets"),
                new("--gaia-flags <hex>", "gaia-flags", Default: "0x00", Description: "GAIA flags byte to use for the probe packets")
            ]),
        new(
            ["auth send-email"],
            static () => new ActionAuthSendEmail(),
            Example: "auth send-email --email <address>",
            HelpText: "Request a six-digit email login code from the Nuraphone backend.",
            Arguments:
            [
                new("--email <address>", "email", Optional: false, Description: "Email address for the login code")
            ]),
        new(
            ["auth verify-code"],
            static () => new ActionAuthVerifyCode(),
            Example: "auth verify-code --code <6-digit-code> [--email <address>]",
            HelpText: "Verify the emailed login code and persist the authenticated session.",
            Arguments:
            [
                new("--code <6-digit-code>", "code", Optional: false, Description: "Six-digit email login code"),
                new("--email <address>", "email", Description: "Optional email override if not already stored")
            ]),
        new(
            ["auth validate-token"],
            static () => new ActionAuthValidateToken(),
            Example: "auth validate-token [--with-app-context] [--app-start-time <unix-ms>]",
            HelpText: "Validate the saved backend auth headers, optionally with the same app/device context used for app-session.",
            Arguments:
            [
                new("--with-app-context", "with-app-context", Description: "Include the current Android/app/device spoof payload with validate_token"),
                new("--app-start-time <unix-ms>", "app-start-time", Description: "Override app_start_time when using --with-app-context")
            ]),
        new(
            ["auth upgrade-info"],
            static () => new ActionAuthUpgradeInfo(),
            Example: "auth upgrade-info [--source <auto|cached|app-session|validate-token|upgrade-entry>] [--upgrade-endpoint <path>] [--language <code>] [--firmware-version <int>] [--app-start-time <unix-ms>]",
            HelpText: "Extract firmware upgrade metadata from cached or freshly fetched automated responses, and trace any typed actions returned by the real upgrade flow.",
            Arguments:
            [
                new("--source <mode>", "source", Default: "auto", Description: "Where to read upgrade metadata from", AllowedValues: ["auto", "cached", "app-session", "validate-token", "upgrade-entry"]),
                new("--upgrade-endpoint <path>", "upgrade-endpoint", Default: "upgrade", Description: "Automated upgrade endpoint to call for upgrade-entry mode"),
                new("--language <code>", "language", Default: "en", Description: "Device language code for upgrade-entry mode"),
                new("--firmware-version <int>", "firmware-version", Description: "Optional firmware version override; if provided, start a fresh backend bluetooth session using the configured serial before calling upgrade"),
                new("--app-start-time <unix-ms>", "app-start-time", Description: "Override app_start_time for refresh modes")
            ]),
        new(
            ["auth upgrade-dump"],
            static () => new ActionAuthUpgradeDump(),
            Example: "auth upgrade-dump [--upgrade-endpoint <path>] [--language <code>] [--session <int>] [--firmware-version <int>] [--output-dir <path>]",
            HelpText: "Call the real automated classic upgrade entry but stop at the returned action list, dumping the raw response and every returned packet payload to disk without executing any Bluetooth packets.",
            Arguments:
            [
                new("--upgrade-endpoint <path>", "upgrade-endpoint", Default: "upgrade", Description: "Automated upgrade endpoint to call"),
                new("--language <code>", "language", Default: "en", Description: "Device language code to send with the upgrade request"),
                new("--session <int>", "session", Description: "Optional explicit backend bluetooth session id override"),
                new("--firmware-version <int>", "firmware-version", Description: "Optional firmware version override; if provided, start a fresh backend bluetooth session using the configured serial before calling upgrade"),
                new("--output-dir <path>", "output-dir", Description: "Optional output directory for dumped files; defaults to a timestamped folder under logs")
            ]),
        new(
            ["auth language-dump"],
            static () => new ActionAuthLanguageDump(),
            Example: "auth language-dump [--language <code>] [--payload-json <path>] [--manual] [--endpoint <path>] [--packets-json <path>] [--session <int>] [--output-dir <path>]",
            HelpText: "Safe default: run app-session/auth/bootstrap through session/start_4, submit analytics, send only the change_language prepare packets to the headset, call change_language_1, dump the returned bulk payload, and stop before any bulk transfer packets are sent. Use --manual or continuation args to keep the old one-endpoint dump behavior.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "change_language", Description: "Automated language endpoint to call, such as change_language, change_language_1, change_language_2, or change_language_3"),
                new("--language <code>", "language", Default: "de", Description: "Target device language code for the initial change_language request when --payload-json is not supplied"),
                new("--payload-json <path>", "payload-json", Description: "Optional json object to merge into the automated request body instead of the default language payload"),
                new("--prepare-log-path <path>", "prepare-log-path", Description: "Full NURA_FRIDA log to use for captured prepare-stage headset responses instead of sending prepare packets to the live headset"),
                new("--packets-json <path>", "packets-json", Description: "Optional json array/object containing the packets to post back to the backend for continuation endpoints"),
                new("--manual", "manual", Description: "Skip the full safe flow and only call the requested endpoint using saved/explicit state"),
                new("--overall-timeout-ms <int>", "overall-timeout-ms", Default: "180000", Description: "Overall timeout for the full safe flow"),
                new("--session <int>", "session", Description: "Optional explicit backend bluetooth session id override"),
                new("--firmware-version <int>", "firmware-version", Description: "Optional firmware version override; if provided, start a fresh backend bluetooth session using the configured serial before calling change_language"),
                new("--output-dir <path>", "output-dir", Description: "Optional output directory for dumped files; defaults to a timestamped folder under logs")
            ]),
        new(
            ["auth language-dump-static"],
            static () => new ActionAuthLanguageDumpStatic(),
            Example: "auth language-dump-static [--endpoint <path>] [--language <code>] [--log-path <path>] [--session <int>] [--firmware-version <int>] [--output-dir <path>]",
            HelpText: "Call a change_language automated endpoint using statically captured Bluetooth response packets from the logged official-app language-update flow instead of a live headset. For continuation endpoints, pass --log-path to the full NURA_FRIDA log so the packets can be derived automatically.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "change_language_1", Description: "Automated language endpoint to call. Use change_language for the initial request or change_language_1/change_language_2/change_language_3 for static continuation replay"),
                new("--language <code>", "language", Default: "ja", Description: "Target device language code when calling the initial change_language endpoint"),
                new("--log-path <path>", "log-path", Description: "Full NURA_FRIDA log containing the captured Bluetooth rx/tx lines used for static continuation replay. Required for change_language_1/change_language_2/change_language_3"),
                new("--session <int>", "session", Description: "Optional explicit backend bluetooth session id override"),
                new("--firmware-version <int>", "firmware-version", Description: "Optional firmware version override; if provided, start a fresh backend bluetooth session using the configured serial before calling change_language"),
                new("--output-dir <path>", "output-dir", Description: "Optional output directory for dumped files; defaults to a timestamped folder under logs")
            ]),
        new(
            ["auth user-session-log"],
            static () => new ActionAuthUserSessionLog(),
            Example: "auth user-session-log [--endpoint <path>] [--usid <int>]",
            HelpText: "Probe the authenticated user-session endpoint and persist any returned user session identifiers.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "user_session/log", Description: "Endpoint candidate to test"),
                new("--usid <int>", "usid", Description: "Optional user session id payload to test")
            ]),
        new(
            ["auth session-log"],
            static () => new ActionAuthSessionLog(),
            Example: "auth session-log [--endpoint <path>] [--session <int>] [--payload-json <path>]",
            HelpText: "Probe the authenticated bluetooth/session log endpoint and persist any returned session identifiers.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "session/log", Description: "Endpoint candidate to test"),
                new("--session <int>", "session", Description: "Optional session id payload to test"),
                new("--payload-json <path>", "payload-json", Description: "Optional json object to use as the request payload; if --session is also supplied it overrides the payload's session field")
            ]),
        new(
            ["auth app-session", "auth app-start-cold"],
            static () => new ActionAuthAppSession(),
            Example: "auth app-session [--endpoint <path>] [--app-start-time <unix-ms>]",
            HelpText: "Call the app-session bootstrap route and persist any returned session identifiers.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "app/session", Description: "Endpoint candidate to test"),
                new("--app-start-time <unix-ms>", "app-start-time", Description: "Override app_start_time in Unix milliseconds")
            ]),
        new(
            ["auth session-start"],
            static () => new ActionAuthSessionStart(),
            Example: "auth session-start --serial <int> --firmware-version <int> [--max-packet-length <int>] [--usid <int>]",
            HelpText: "Call the end_to_end/session/start backend bootstrap endpoint using a real user session id.",
            Arguments:
            [
                new("--serial <int>", "serial", Optional: false, Description: "Headset serial number"),
                new("--firmware-version <int>", "firmware-version", Optional: false, Description: "Headset firmware version"),
                new("--max-packet-length <int>", "max-packet-length", Default: "182", Description: "Maximum packet length hint"),
                new("--max-bulk-packet-length <int>", "max-bulk-packet-length", Default: "0", Description: "Maximum bulk packet length hint"),
                new("--usid <int>", "usid", Description: "Explicit user session id override if auth state does not have one")
            ]),
        new(
            ["auth session-start-static"],
            static () => new ActionAuthSessionStartStatic(),
            Example: "auth session-start-static --log-path <path> [--session <int>] [--output-dir <path>]",
            HelpText: "Replay the captured official-app session/start_1/_2/_3/_4 continuation packets against a fresh backend bluetooth session, without using a live headset.",
            Arguments:
            [
                new("--log-path <path>", "log-path", Optional: false, Description: "Full NURA_FRIDA log containing the captured Bluetooth rx/tx lines used for static continuation replay"),
                new("--session <int>", "session", Description: "Optional explicit backend bluetooth session id override; otherwise uses the saved auth state session"),
                new("--output-dir <path>", "output-dir", Description: "Optional root output directory for dumped response files")
            ]),
        new(
            ["auth session-start-continue", "auth session-start-1"],
            static () => new ActionAuthSessionStartContinue(),
            Example: "auth session-start-continue --response-hex <hex> [--endpoint <path>] [--session <int>]",
            HelpText: "Post headset bootstrap response bytes back to the backend call-home endpoint, starting with session/start_1.",
            Arguments:
            [
                new("--response-hex <hex>", "response-hex", Optional: false, Description: "Headset response bytes from the previous bootstrap packet, usually payload_ex_status only"),
                new("--endpoint <path>", "endpoint", Default: "last_response.final_event or session/start_1", Description: "Continuation endpoint override"),
                new("--session <int>", "session", Description: "Explicit backend bluetooth/session id override")
            ]),
        new(
            ["auth session-start-next"],
            static () => new ActionAuthSessionStartNext(),
            Example: "auth session-start-next [--endpoint <path>] [--session <int>] [--read-timeout-ms <int>] [--gaia-version <int>] [--gaia-flags <hex>]",
            HelpText: "Run the next backend-provided bootstrap packet against the headset and immediately post the headset response back to the next continuation endpoint.",
            Arguments:
            [
                new("--endpoint <path>", "endpoint", Default: "last_response.final_event", Description: "Continuation endpoint override"),
                new("--session <int>", "session", Description: "Explicit backend bluetooth/session id override"),
                new("--read-timeout-ms <int>", "read-timeout-ms", Default: "10000", Description: "Timeout for the headset response frame"),
                new("--gaia-version <int>", "gaia-version", Default: "1", Description: "GAIA version to use for local headset packets"),
                new("--gaia-flags <hex>", "gaia-flags", Default: "0x00", Description: "GAIA flags byte to use for local headset packets")
            ]),
        new(
            ["auth session-start-u-only"],
            static () => new ActionAuthSessionStartUOnly(),
            Example: "auth session-start-u-only [--read-timeout-ms <int>] [--gaia-version <int>] [--gaia-flags <hex>]",
            HelpText: "Execute only the saved unencrypted session/start packets locally and stop before the run-packet phase.",
            Arguments:
            [
                new("--read-timeout-ms <int>", "read-timeout-ms", Default: "10000", Description: "Timeout for the headset response frame"),
                new("--gaia-version <int>", "gaia-version", Default: "1", Description: "GAIA version to use for local headset packets"),
                new("--gaia-flags <hex>", "gaia-flags", Default: "0x00", Description: "GAIA flags byte to use for local headset packets")
            ]),
        new(
            ["auth show-state"],
            static () => new ActionAuthShowState(),
            Example: "auth show-state",
            HelpText: "Print the stored backend auth state.")
    ];

    internal static ActionDescriptor? Find(string commandName) {
        return Actions.FirstOrDefault(action =>
            action.Commands.Any(command => string.Equals(command, commandName, StringComparison.OrdinalIgnoreCase)));
    }

    internal static string ResolveCommandName(string[] args) {
        if (args.Length >= 3) {
            var groupedCommand = $"{args[0].ToLowerInvariant()} {args[1].ToLowerInvariant()} {args[2].ToLowerInvariant()}";
            if (Find(groupedCommand) is not null) {
                return groupedCommand;
            }
        }

        if (args.Length >= 2) {
            var groupedCommand = $"{args[0].ToLowerInvariant()} {args[1].ToLowerInvariant()}";
            if (Find(groupedCommand) is not null) {
                return groupedCommand;
            }
        }

        return args[0].ToLowerInvariant();
    }

    internal static void PrintHelp() {
        Console.WriteLine();
        WriteColor("NuraDesktopApp", ConsoleColor.Cyan, true);
        Console.WriteLine("Reverse-engineering local control workbench for Nuraphone headphones.");
        Console.WriteLine();
        WriteColor("Usage", ConsoleColor.Green);
        Console.WriteLine("  NuraDesktopApp.exe <command> [subcommand] [options]");
        Console.WriteLine();
        WriteColor("Commands", ConsoleColor.Green);
        Console.WriteLine();

        var commandWidth = Actions.Max(action => action.Commands[0].Length) + 2;
        foreach (var action in Actions) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  {action.Commands[0].PadRight(commandWidth)}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(action.Example);
            Console.ResetColor();

            Console.Write("    ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(action.HelpText);
            Console.ResetColor();

            if (action.Arguments is { Length: > 0 }) {
                foreach (var argument in action.Arguments) {
                    Console.Write("      ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(argument.Flag);
                    Console.ResetColor();

                    Console.Write(argument.Optional ? " [optional]" : " [required]");
                    if (!string.IsNullOrWhiteSpace(argument.Default)) {
                        Console.Write($" (default: {argument.Default})");
                    }

                    if (argument.AllowedValues is { Length: > 0 }) {
                        Console.Write($" (allowed: {string.Join("|", argument.AllowedValues)})");
                    }

                    if (!string.IsNullOrWhiteSpace(argument.Description)) {
                        Console.Write($" {argument.Description}");
                    }

                    Console.WriteLine();
                }
            }

            Console.WriteLine();
        }
    }

    private static void WriteColor(string text, ConsoleColor color, bool newLine = false) {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
        if (newLine) {
            Console.WriteLine();
        }
    }
}
