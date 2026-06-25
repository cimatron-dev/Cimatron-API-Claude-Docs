using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Installer
{
    // Cimatron API plugin installer. Placeholders are substituted at packaging
    // time by the /package-installer slash command - do not edit by hand.
    internal static class Program
    {
        private const string ApiName = "@@API_NAME@@";
        private const string DllName = "@@DLL_NAME@@";
        private const string PluginClass = "@@PLUGIN_CLASS@@";
        private const string PluginVersion = "@@VERSION@@";
        private const string TargetVersion = "@@TARGET_VERSION@@";
        private const bool HasIcon = @@HAS_ICON@@;

        // Cimatron ships under several product folders beneath these shared bases:
        // the full CAD product as "Cimatron", and the quoting tool as "Cimatron
        // DieQuote". Each has its own <version> subfolders under Program Files and
        // its own mirror under ProgramData. We discover across every known variant
        // and derive the ProgramData INI path from whichever install the user
        // targets, so a DieQuote install never gets registered against the full
        // product's INI (or vice versa).
        private const string CimatronInstallBase = @"C:\Program Files\Cimatron";
        private const string CimatronProgramDataBase = @"C:\ProgramData\Cimatron";
        private static readonly string[] CimatronProductVariants = { "Cimatron", "Cimatron DieQuote" };

        private const int ExitOk = 0;
        private const int ExitUserCancelled = 1;
        private const int ExitNoMatchingCimatron = 2;
        private const int ExitDllLocked = 3;
        private const int ExitWriteFailed = 4;

        private static int Main(string[] args)
        {
            try
            {
                bool uninstall = args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));
                string explicitRoot = ParseOption(args, "--root");

                PrintBanner(uninstall);

                var versions = explicitRoot != null
                    ? new[] { NormalizeProgramFolder(explicitRoot) }
                    : DiscoverCimatronVersions();

                if (versions.Length == 0)
                {
                    Console.Error.WriteLine("No usable Cimatron installation found.");
                    Console.Error.WriteLine("  Searched:");
                    foreach (var variant in CimatronProductVariants)
                        Console.Error.WriteLine($"    {Path.Combine(CimatronInstallBase, variant)}\\<version>\\Program\\");
                    if (!string.Equals(TargetVersion, "any", StringComparison.OrdinalIgnoreCase))
                        Console.Error.WriteLine($"  Required version: {TargetVersion}");
                    return WaitAndReturn(ExitNoMatchingCimatron);
                }

                string chosenRoot = ChooseTarget(versions);
                if (chosenRoot == null) return WaitAndReturn(ExitUserCancelled);

                string version = InferVersionFromRoot(chosenRoot);

                if (uninstall)
                    DoUninstall(chosenRoot, version);
                else
                    DoInstall(chosenRoot, version);

                return WaitAndReturn(ExitOk);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"ERROR: access denied - {ex.Message}");
                Console.Error.WriteLine("This usually means Cimatron is running and holds a file lock,");
                Console.Error.WriteLine("or the installer was launched without administrator privileges.");
                return WaitAndReturn(ExitWriteFailed);
            }
            catch (IOException ex) when ((uint)ex.HResult == 0x80070020 /* SHARING_VIOLATION */)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"ERROR: file locked - {ex.Message}");
                Console.Error.WriteLine("Close Cimatron and re-run the installer.");
                return WaitAndReturn(ExitDllLocked);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("ERROR: " + ex);
                return WaitAndReturn(ExitWriteFailed);
            }
        }

        private static void PrintBanner(bool uninstall)
        {
            Console.WriteLine();
            Console.WriteLine($"{ApiName} {PluginVersion} - Cimatron API plugin {(uninstall ? "uninstaller" : "installer")}");
            Console.WriteLine(new string('-', 60));
        }

        private static string[] DiscoverCimatronVersions()
        {
            if (!Directory.Exists(CimatronInstallBase))
                return new string[0];

            var matches = new List<string>();
            foreach (var variant in CimatronProductVariants)
            {
                string variantRoot = Path.Combine(CimatronInstallBase, variant);
                if (!Directory.Exists(variantRoot)) continue;

                foreach (var dir in Directory.GetDirectories(variantRoot))
                {
                    string name = Path.GetFileName(dir);
                    if (!IsVersionLikeName(name)) continue;
                    if (!IsAtLeast2024(name)) continue;
                    if (!string.Equals(TargetVersion, "any", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, TargetVersion, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string program = Path.Combine(dir, "Program");
                    if (!Directory.Exists(program)) continue;
                    matches.Add(program);
                }
            }

            // Newest version first; ties broken by the declared variant order
            // (full Cimatron ahead of DieQuote) so the default [1] pick is stable.
            matches.Sort((a, b) =>
            {
                int byVersion = string.Compare(InferVersionFromRoot(b), InferVersionFromRoot(a), StringComparison.Ordinal);
                if (byVersion != 0) return byVersion;
                return VariantRank(InferVariantFromRoot(a)).CompareTo(VariantRank(InferVariantFromRoot(b)));
            });
            return matches.ToArray();
        }

        private static int VariantRank(string variant)
        {
            int idx = Array.IndexOf(CimatronProductVariants, variant);
            return idx < 0 ? int.MaxValue : idx;
        }

        private static bool IsVersionLikeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split('.');
            if (parts.Length != 2) return false;
            return parts[0].Length == 4 && parts[0].All(char.IsDigit) && parts[1].All(char.IsDigit);
        }

        private static bool IsAtLeast2024(string versionName)
        {
            var parts = versionName.Split('.');
            return int.TryParse(parts[0], out int year) && year >= 2024;
        }

        private static string InferVersionFromRoot(string programFolder)
        {
            // <root>\Program  -> <root>; Path.GetFileName(<root>) is the version literal.
            var parent = Directory.GetParent(programFolder);
            return parent != null ? parent.Name : "unknown";
        }

        private static string InferVariantFromRoot(string programFolder)
        {
            // <base>\<variant>\<version>\Program -> the <variant> folder name. This is
            // the same name that appears under ProgramData, so it drives GetIniPath.
            // Defaults to "Cimatron" for an off-layout --root override.
            var versionDir = Directory.GetParent(programFolder);
            var variantDir = versionDir != null ? versionDir.Parent : null;
            return variantDir != null ? variantDir.Name : "Cimatron";
        }

        private static string Describe(string programFolder)
        {
            return $"{InferVariantFromRoot(programFolder)} {InferVersionFromRoot(programFolder)}";
        }

        private static string NormalizeProgramFolder(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("--root cannot be empty");
            string trimmed = root.Trim().TrimEnd('\\', '/');
            if (!trimmed.EndsWith("Program", StringComparison.OrdinalIgnoreCase))
                trimmed = Path.Combine(trimmed, "Program");
            if (!Directory.Exists(trimmed))
                throw new DirectoryNotFoundException($"--root '{root}' resolved to '{trimmed}', which does not exist.");
            return trimmed;
        }

        private static string ChooseTarget(string[] candidates)
        {
            if (candidates.Length == 1)
            {
                Console.WriteLine($"Target Cimatron: {Describe(candidates[0])}");
                return candidates[0];
            }
            Console.WriteLine("Multiple Cimatron installations found:");
            for (int i = 0; i < candidates.Length; i++)
                Console.WriteLine($"  [{i + 1}] {Describe(candidates[i])}   ({candidates[i]})");
            Console.Write($"Pick one [1-{candidates.Length}] (default 1, q to cancel): ");
            string answer = Console.ReadLine();
            if (answer != null && (answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)
                                || answer.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)))
                return null;
            if (string.IsNullOrWhiteSpace(answer)) return candidates[0];
            if (int.TryParse(answer.Trim(), out int idx) && idx >= 1 && idx <= candidates.Length)
                return candidates[idx - 1];
            Console.WriteLine("Unrecognized selection - defaulting to [1].");
            return candidates[0];
        }

        private static string ParseOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        // ---------------- Install ----------------

        private static void DoInstall(string programFolder, string version)
        {
            Console.WriteLine();
            Console.WriteLine($"Installing into {programFolder}");
            Console.WriteLine();

            var payload = EnumeratePayload();
            if (payload.Count == 0)
                throw new InvalidOperationException("Installer is missing its embedded payload - repackage with /package-installer.");

            foreach (var entry in payload)
            {
                string dest = Path.Combine(programFolder, entry.Key);
                Console.WriteLine($"  -> {dest}");
                WritePayloadFile(dest, entry.Value);
            }

            string iniPath = GetIniPath(programFolder, version);
            Console.WriteLine();
            Console.WriteLine($"Registering in {iniPath}");
            UpsertIniEntry(iniPath, PluginClass);

            Console.WriteLine();
            Console.WriteLine($"Done. Launch {Describe(programFolder)} - the new command will appear in the toolbar.");
            if (!HasIcon)
                Console.WriteLine("(No icon was packaged. The plugin will use whatever IconSource resolves to at runtime.)");
        }

        private static void WritePayloadFile(string destPath, byte[] data)
        {
            string dir = Path.GetDirectoryName(destPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Overwrite if present. If the file is locked by CimatronE.exe, this throws
            // IOException with HRESULT 0x80070020; Main's catch turns that into a friendly message.
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                fs.Write(data, 0, data.Length);
        }

        // ---------------- Uninstall ----------------

        private static void DoUninstall(string programFolder, string version)
        {
            Console.WriteLine();
            Console.WriteLine($"Uninstalling from {programFolder}");
            Console.WriteLine();

            var payload = EnumeratePayload();
            int removed = 0;
            foreach (var entry in payload)
            {
                string target = Path.Combine(programFolder, entry.Key);
                if (File.Exists(target))
                {
                    Console.WriteLine($"  removing {target}");
                    File.Delete(target);
                    removed++;
                }
                else
                {
                    Console.WriteLine($"  skip (not present) {target}");
                }
            }

            string iniPath = GetIniPath(programFolder, version);
            Console.WriteLine();
            Console.WriteLine($"De-registering from {iniPath}");
            RemoveIniEntry(iniPath, PluginClass);

            Console.WriteLine();
            Console.WriteLine($"Done. {removed} file(s) removed. ExternalCommands.ini cleaned. " +
                              "Cimatron will stop loading the plugin on its next launch.");
        }

        // ---------------- Embedded payload helpers ----------------

        private static Dictionary<string, byte[]> EnumeratePayload()
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var resourceName in asm.GetManifestResourceNames())
            {
                const string prefix = "Installer.Payload.";
                if (!resourceName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string filename = resourceName.Substring(prefix.Length);
                using (var s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s == null) continue;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        result[filename] = ms.ToArray();
                    }
                }
            }
            return result;
        }

        // ---------------- INI mutation ----------------

        private static string GetIniPath(string programFolder, string version)
        {
            // Mirror the install variant into ProgramData: a "Cimatron DieQuote"
            // install writes to C:\ProgramData\Cimatron\Cimatron DieQuote\<version>\...
            string variant = InferVariantFromRoot(programFolder);
            return Path.Combine(CimatronProgramDataBase, variant, version, "Data", "ExternalCommands.ini");
        }

        private static void UpsertIniEntry(string iniPath, string pluginClass)
        {
            var lines = ReadIniLines(iniPath, out Encoding enc);
            int sectionStart = FindOrCreateSection(lines, "[Plugin Ext Commands]");

            string desiredLine = $"{pluginClass}={pluginClass}@1";
            int existing = FindKeyInSection(lines, sectionStart, pluginClass);
            if (existing >= 0)
            {
                if (!lines[existing].Equals(desiredLine, StringComparison.Ordinal))
                {
                    Console.WriteLine($"  updating: {lines[existing]}  ->  {desiredLine}");
                    lines[existing] = desiredLine;
                }
                else
                {
                    Console.WriteLine($"  (already present) {desiredLine}");
                }
            }
            else
            {
                int insertAt = FindEndOfSection(lines, sectionStart);
                lines.Insert(insertAt, desiredLine);
                Console.WriteLine($"  added: {desiredLine}");
            }

            WriteIniLines(iniPath, lines, enc);
        }

        private static void RemoveIniEntry(string iniPath, string pluginClass)
        {
            if (!File.Exists(iniPath))
            {
                Console.WriteLine("  (INI not present - nothing to remove)");
                return;
            }

            var lines = ReadIniLines(iniPath, out Encoding enc);
            int sectionStart = FindSection(lines, "[Plugin Ext Commands]");
            if (sectionStart < 0)
            {
                Console.WriteLine("  ([Plugin Ext Commands] section not found - nothing to remove)");
                return;
            }
            int existing = FindKeyInSection(lines, sectionStart, pluginClass);
            if (existing < 0)
            {
                Console.WriteLine($"  ({pluginClass} not present - nothing to remove)");
                return;
            }
            Console.WriteLine($"  removing: {lines[existing]}");
            lines.RemoveAt(existing);
            WriteIniLines(iniPath, lines, enc);
        }

        private static List<string> ReadIniLines(string iniPath, out Encoding enc)
        {
            if (!File.Exists(iniPath))
            {
                enc = new UTF8Encoding(true);
                // Canonical skeleton mirrors /register-command's skeleton - the
                // leading comment is what Cimatron's loader uses to recognize the file.
                return new List<string>
                {
                    ";ExternalCommands.ini",
                    "",
                    "[Global Flags]",
                    "ResetApiCommands =1",
                    "",
                    "[COM Ext Commands]",
                    "",
                    "[Plugin Ext Commands]",
                    "",
                    "[External Pane]",
                };
            }

            // Detect BOM. Cimatron-written INIs are commonly UTF-8 with BOM.
            byte[] bytes = File.ReadAllBytes(iniPath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                enc = new UTF8Encoding(true);
            else
                enc = new UTF8Encoding(false);

            string text = enc.GetString(bytes, 0, bytes.Length);
            // Strip the BOM character that GetString surfaces when the BOM is present -
            // we want to preserve newlines but not double-encode the BOM on write.
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            // Preserve line endings: split on \r?\n and remember whether the file ended with one.
            var raw = new List<string>(text.Split('\n'));
            for (int i = 0; i < raw.Count; i++)
                if (raw[i].Length > 0 && raw[i][raw[i].Length - 1] == '\r')
                    raw[i] = raw[i].Substring(0, raw[i].Length - 1);
            // If the file ended with a newline, we'll have a trailing empty entry. Drop it
            // and add it back at write time so we don't accumulate trailing blank lines.
            if (raw.Count > 0 && raw[raw.Count - 1].Length == 0)
                raw.RemoveAt(raw.Count - 1);
            return raw;
        }

        private static void WriteIniLines(string iniPath, List<string> lines, Encoding enc)
        {
            string dir = Path.GetDirectoryName(iniPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string joined = string.Join("\r\n", lines) + "\r\n";
            File.WriteAllText(iniPath, joined, enc);
        }

        private static int FindSection(List<string> lines, string header)
        {
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static int FindOrCreateSection(List<string> lines, string header)
        {
            int idx = FindSection(lines, header);
            if (idx >= 0) return idx;
            // Insert before [External Pane] if present, otherwise at end of file.
            int paneAt = FindSection(lines, "[External Pane]");
            int insertAt = paneAt >= 0 ? paneAt : lines.Count;
            lines.Insert(insertAt, header);
            lines.Insert(insertAt + 1, "");
            return insertAt;
        }

        private static int FindEndOfSection(List<string> lines, int sectionStart)
        {
            for (int i = sectionStart + 1; i < lines.Count; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("[") && t.Contains("]")) return i;
            }
            return lines.Count;
        }

        private static int FindKeyInSection(List<string> lines, int sectionStart, string key)
        {
            int end = FindEndOfSection(lines, sectionStart);
            for (int i = sectionStart + 1; i < end; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                string k = t.Substring(0, eq).Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static int WaitAndReturn(int code)
        {
            // When launched by double-click the console window closes immediately on exit.
            // Pause so the user can read the result. When run from a terminal that's already
            // open, Environment.UserInteractive is still true - that's the right behavior;
            // the user just hits Enter to move on.
            if (Environment.UserInteractive)
            {
                Console.WriteLine();
                Console.WriteLine("Press Enter to exit...");
                try { Console.ReadLine(); } catch { /* redirected stdin - ignore */ }
            }
            return code;
        }
    }
}
