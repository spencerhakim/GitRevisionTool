using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unclassified;
using System.Diagnostics;
using Microsoft.Win32;

namespace GitRevisionTool
{
	class Program
	{
		static string revision;
		static bool debugOutput;
		static bool isModified;
		static DateTimeOffset revTime;

		static int Main(string[] args)
		{
			//define command line args
			CommandLineParser clp = new CommandLineParser();
			clp.AddKnownOption("f", "format", true);
			clp.AddKnownOption("h", "help");
			clp.AddKnownOption("i", "ignore-missing");
			clp.AddKnownOption("M", "stop-if-modified");
			clp.AddKnownOption("r", "revision");
			clp.AddKnownOption("v", "version");
			clp.AddKnownOption("D", "debug");

			//initialize vars from args
			debugOutput = clp.IsOptionSet("D");
			bool showRevision = clp.IsOptionSet("r");
			bool ignoreMissing = clp.IsOptionSet("i");
			bool stopIfModified = clp.IsOptionSet("M");
			string inputFile = clp.GetArgument(0);
			string outputFile = clp.GetArgument(1);
			string customFormat = "{!}{commit}";
			if( clp.IsOptionSet("f") )
				customFormat = clp.GetOptionValue("f");

			//show help/version
			if( clp.IsOptionSet("h") || clp.IsOptionSet("v") )
			{
				HandleHelp(clp.IsOptionSet("h"));
				return 0;
			}

			//default action
			if( String.IsNullOrWhiteSpace(inputFile) && String.IsNullOrWhiteSpace(outputFile) && !showRevision )
				showRevision = true;

			if( String.IsNullOrWhiteSpace(inputFile) )
				inputFile = Environment.CurrentDirectory + @"\.";

			if( debugOutput )
				Console.Error.WriteLine("Working on path " + Path.GetDirectoryName(inputFile) );

			//process Git info and populate revision and revTime
			ProcessDirectory(Path.GetDirectoryName(inputFile), ignoreMissing);

			//show git revision
			if( showRevision )
			{
				if( revision == null )
				{
					if( ignoreMissing )
					{
						revision = "0000000000000000000000000000000000000000";
						revTime = DateTimeOffset.Now;
					}
					else
					{
						Console.Error.WriteLine("Error: Not a Git working directory.");
						return 1;
					}
				}

				Console.WriteLine( ResolveFormat(customFormat) );
				return 0;
			}

			///////////////////////////////////////////////////////////////////////////////////////////////////////////
			// Main processing
			///////////////////////////////////////////////////////////////////////////////////////////////////////////
			if( revision == null )
			{
				if( ignoreMissing )
				{
					revision = "0000000000000000000000000000000000000000";
					revTime = DateTimeOffset.Now;
				}
				else
				{
					Console.Error.WriteLine("Error: Not a Git working directory.");
					return 1;
				}
			}

			if( stopIfModified && isModified )
			{
				Console.Error.WriteLine("Error: Git working directory contains uncommited changes, stop requested by option.");
				return 1;
			}

			if( !File.Exists(inputFile) )
			{
				Console.Error.WriteLine("Error: Input file doesn't exist.");
				return 1;
			}

			if( debugOutput )
				Console.Error.WriteLine("Patching " + Path.GetFileName(inputFile) + "...");

			string attrStart = null, attrEnd = null;
			switch( Path.GetExtension(outputFile).ToLower() )
			{
				case ".cs":
					attrStart = "[";
					attrEnd = "]";
					break;

				case ".vb":
					attrStart = "<";
					attrEnd = ">";
					break;

				default:
					Console.Error.WriteLine("Logic error: invalid AssemblyInfo file extension: " + Path.GetExtension(outputFile).ToLower());
					return 1;
			}

			using( StreamReader sr = new StreamReader(inputFile, Encoding.Default, true) )
			{
				sr.Peek();
				using( StreamWriter sw = new StreamWriter(outputFile, false, sr.CurrentEncoding) )
				{
					while( !sr.EndOfStream )
					{
						string line = sr.ReadLine();

						Match m;
						m = Regex.Match(
							line,
							@"^(\s*\" + attrStart + @"\s*assembly\s*:\s*AssemblyInformationalVersion\s*\(\s*"")(.*)(""\s*\)\s*\" + attrEnd + @".*)$",
							RegexOptions.IgnoreCase);
						if( m.Success )
						{
							string value = m.Groups[2].Value;
							value = ResolveFormat(value);
							line = m.Groups[1].Value + value + m.Groups[3].Value;
							if( debugOutput )
								Console.Error.WriteLine("Found AssemblyInformationalVersion attribute");
						}

						sw.WriteLine(line);
					}
				}

				if( debugOutput )
					Console.Error.WriteLine("Patched " + Path.GetFileName(inputFile));
			}

			return 0;
		}

		static string ResolveFormat(string value)
		{
			value = value.Replace("{!}", isModified ? "!" : "");
			value = value.Replace("{commit}", revision);
			value = value.Replace("{date}", revTime.ToString("yyyyMMdd"));
			value = value.Replace("{date:ymd-}", revTime.ToString("yyyy-MM-dd"));
			value = value.Replace("{time}", revTime.ToString("HHmmss"));
			value = value.Replace("{time:hms}", revTime.ToString("HHmmss"));
			value = value.Replace("{time:hms:}", revTime.ToString("HH:mm:ss"));
			value = value.Replace("{time:hm}", revTime.ToString("HHmm"));
			value = value.Replace("{time:hm:}", revTime.ToString("HH:mm"));
			value = value.Replace("{time:h}", revTime.ToString("HH"));
			value = value.Replace("{time:o}", revTime.ToString("%K"));
			value = Regex.Replace(value, @"\{!:(.*?)\}", delegate(Match m) { return isModified ? m.Groups[1].Value : ""; });
			value = Regex.Replace(value, @"\{commit:(40|[1-3][0-9]|[5-9])\}", delegate(Match m) { return revision.Substring(0, int.Parse(m.Groups[1].Value)); });
			value = Regex.Replace(value, @"\{xmin:([0-9]{4})\}", delegate(Match m) { return HexMinutes(int.Parse(m.Groups[1].Value), 1); });
			value = Regex.Replace(value, @"\{xmin:([0-9]{4}):([0-9]{1,2})\}", delegate(Match m) { return HexMinutes(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)); });
			return value;
		}

		static string HexMinutes(int baseYear, int length)
		{
			int min = (int)(revTime - new DateTime(baseYear, 1, 1)).TotalMinutes;
			if( min < 0 )
				return "-" + (-min).ToString("x" + length);
			else
				return min.ToString("x" + length);
		}

		static void HandleHelp(bool showHelp)
		{
			string productName = "";
			string productVersion = "";
			string productDescription = "";
			string productCopyright = "";
			string appFilename = "";

			object[] customAttributes = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
			if( customAttributes != null && customAttributes.Length > 0 )
			{
				productName = ((AssemblyProductAttribute)customAttributes[0]).Product;
			}
			customAttributes = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
			if( customAttributes != null && customAttributes.Length > 0 )
			{
				productVersion = ((AssemblyFileVersionAttribute)customAttributes[0]).Version;
			}
			customAttributes = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
			if( customAttributes != null && customAttributes.Length > 0 )
			{
				productDescription = ((AssemblyDescriptionAttribute)customAttributes[0]).Description;
			}
			customAttributes = Assembly.GetEntryAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
			if( customAttributes != null && customAttributes.Length > 0 )
			{
				productCopyright = ((AssemblyCopyrightAttribute)customAttributes[0]).Copyright;
			}

			Console.WriteLine(productName + " " + productVersion);
			Console.WriteLine(productCopyright);

			if( showHelp )
			{
				appFilename = Path.GetFileName(Assembly.GetEntryAssembly().Location);

				Console.WriteLine();
				Console.WriteLine(productDescription);
				Console.WriteLine();
				Console.WriteLine("Usage:");
				Console.WriteLine("  " + appFilename + " [options] <input file> <output file>");
				Console.WriteLine();
				Console.WriteLine("Options:");
				Console.WriteLine("  -f, --format    Prints the passed string with the specified format");
				Console.WriteLine("  -i, --ignore-missing");
				Console.WriteLine("                  Continue processing even if not a Git working directory");
				Console.WriteLine("  -M, --stop-if-modified");
				Console.WriteLine("                  Stops if the working copy contains uncommited changes");
				Console.WriteLine("  -r, --revision  Shows the working copy revision");
				Console.WriteLine("  -v, --version   Shows product version");
				Console.WriteLine("  -h, --help      Shows this help page");
				Console.WriteLine("  -D, --debug     Shows debug information");
				Console.WriteLine();
				Console.WriteLine("The input file should point a template version of the AssemblyInfo file inside");
				Console.WriteLine("the Git working directory. The output file should be the AssemblyInfo.cs/vb file"); //80c
				Console.WriteLine();
				Console.WriteLine("To use GitRevisionTool in a C# or VB.NET project to update AssemblyInfo.cs/vb,");
				Console.WriteLine("use this command as a pre-build event in the project settings:");
				Console.WriteLine("GitRevisionTool \"$(ProjectDir)AssemblyInfo.cs.in\" \"$(ProjectDir)AssemblyInfo.cs\""); //80c
				Console.WriteLine();
				Console.WriteLine("The following assembly attributes are supported:");
				Console.WriteLine("  AssemblyInformationalVersion(\"... {commit} {date} {time} ...\")");
				Console.WriteLine();
				Console.WriteLine("The following placeholders are supported:");
				Console.WriteLine("  {!}                Prints ! if modified");
				Console.WriteLine("  {!:<text>}         Prints <text> if modified");
				Console.WriteLine("  {commit}           Prints full commit hash");
				Console.WriteLine("  {commit:<length>}  Prints first <length> chars of commit hash");
				Console.WriteLine("  {date}             Prints commit date as YYYYMMDD");
				Console.WriteLine("  {date:<format>}    Prints commit data with format:");
				Console.WriteLine("                       ymd-   YYYY-MM-DD");
				Console.WriteLine("  {time}             Prints commit time as HHMMS");
				Console.WriteLine("  {time:<format>}    Prints commit time with format:");
				Console.WriteLine("                       hms    HHMMSS");
				Console.WriteLine("                       hms:   HH:MM:SS");
				Console.WriteLine("                       hm     HHMM");
				Console.WriteLine("                       hm:    HH:MM");
				Console.WriteLine("                       h      HH");
				Console.WriteLine("                       o      Time zone like +0100");
				Console.WriteLine("  {xmin:<year>:<length>");
				Console.WriteLine("                     Prints minutes since year <year>, length <length>");
				Console.WriteLine("                     in hexadecimal format");
				Console.WriteLine();
				Console.WriteLine("Example for C#:");
				Console.WriteLine("  [assembly: AssemblyInformationalVersion(\"MyApp {commit:8}/{date}\")]");
				Console.WriteLine("Will be replaced with:");
				Console.WriteLine("  [assembly: AssemblyInformationalVersion(\"MyApp 45d4e32f/20111231\")]");
				Console.WriteLine();
				Console.WriteLine("Example for batch file:");
				Console.WriteLine("  GitRevisionTool --format \"SET revid={commit:8}\" > %temp%\\revid.cmd");
				Console.WriteLine("Will generate file revid.cmd with contents:");
				Console.WriteLine("  SET revid=45d4e32f");
				Console.WriteLine("(Write to a file outside the Git working directory to avoid false modify.)");
				Console.WriteLine();
				Console.WriteLine("Git (msysGit) must be installed in one of %ProgramFiles*%\\Git*.");
				Console.WriteLine();
				Console.WriteLine("ATTENTION: Be sure not to have the AssemblyInfo file opened in the IDE while");
				Console.WriteLine("           building the project, or the version modifications will be ignored");
				Console.WriteLine("           by the compiler.");
			}
		}

		static void ProcessDirectory(string path, bool silent)
		{
			// First try to use the installed git binary
			string gitExec = FindGitBinary();
			if( gitExec == null )
			{
				if( !silent )
					Console.Error.WriteLine("Error: Git not installed or not found.");
				return;
			}

			ProcessStartInfo psi = new ProcessStartInfo(gitExec, "log -n 1 --format=format:\"%H %ci\"");
			psi.WorkingDirectory = path;
			psi.RedirectStandardOutput = true;
			if( silent )
				psi.RedirectStandardError = true;
			psi.UseShellExecute = false;
			Process p = Process.Start(psi);

			string line = null;
			while( !p.StandardOutput.EndOfStream )
			{
				line = p.StandardOutput.ReadLine();
				Match m = Regex.Match(line, @"^([0-9a-fA-F]{40}) ([0-9-]{10} [0-9:]{8} [0-9+-]{5})");
				if( m.Success )
				{
					revision = m.Groups[1].Value;
					revTime = DateTimeOffset.Parse(m.Groups[2].Value);
					p.StandardOutput.ReadToEnd();   // Kindly eat up the remaining output
					break;
				}
			}

			if( !p.WaitForExit(1000) )
				p.Kill();

			if( debugOutput )
			{
				Console.Error.WriteLine("Revision = " + revision);
				Console.Error.WriteLine("RevTime  = " + revTime.ToString());
			}

			if( revision == null )
				return;   // Try no more

			psi = new ProcessStartInfo(gitExec, "status --porcelain");
			psi.WorkingDirectory = path;
			psi.RedirectStandardOutput = true;
			if( silent )
				psi.RedirectStandardError = true;
			psi.UseShellExecute = false;
			p = Process.Start(psi);

			line = null;
			while( !p.StandardOutput.EndOfStream )
			{
				line = p.StandardOutput.ReadLine();
			}

			if( !p.WaitForExit(1000) )
				p.Kill();

			isModified = !String.IsNullOrWhiteSpace(line);

			if( debugOutput )
				Console.Error.WriteLine("isModified = " + isModified.ToString());
		}

		private static string FindGitBinary()
		{
			string git = null;

			// Read registry uninstaller key
			RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Git_is1");
			if( key != null )
			{
				object loc = key.GetValue("InstallLocation");
				if( loc is string )
				{
					git = Path.Combine((string)loc, @"bin\git.exe");
					if( !File.Exists(git) ) git = null;
				}
			}

			// Try 64-bit registry key
			if( git == null && Environment.Is64BitOperatingSystem )
			{
				key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Git_is1");
				if( key != null )
				{
					object loc = key.GetValue("InstallLocation");
					if( loc is string )
					{
						git = Path.Combine((string)loc, @"bin\git.exe");
						if( !File.Exists(git) ) git = null;
					}
				}
			}

			//Search ":\Program Files"
			if( git == null )
			{
				foreach( string dir in Directory.GetDirectories(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "git*") )
				{
					git = Path.Combine(dir, @"bin\git.exe");
					if( !File.Exists(git) ) git = null;
				}
			}

			//Search ":\Program Files (x86)" if last search failed and it's an x64 OS
			if( git == null && Environment.Is64BitOperatingSystem )
			{
				foreach( string dir in Directory.GetDirectories(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "git*") )
				{
					git = Path.Combine(dir, @"bin\git.exe");
					if( !File.Exists(git) ) git = null;
				}
			}

			return git;
		}
	}
}