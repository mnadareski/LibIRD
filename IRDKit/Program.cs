﻿using CommandLine;
using DiscUtils;
using DiscUtils.Iso9660;
using LibIRD;
using SabreTools.RedumpLib.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IRDKit
{
    internal class Program
    {
        #region Options

        /// <summary>
        /// IRD Creation command
        /// </summary>
        [Verb("create", HelpText = "Create an IRD from an ISO")]
        public class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Path to an ISO file, or directory of ISO files")]
            public IEnumerable<string> ISOPath { get; set; }

            [Option('o', "output", HelpText = "Path to the IRD file to be created (will overwrite)")]
            public string IRDPath { get; set; }

            [Option('b', "layerbreak", HelpText = "Layerbreak value in bytes (use with BD-Video hybrid discs). Default: 12219392")]
            public long? Layerbreak {  get; set; }

            [Option('k', "key", HelpText = "Hexadecimal representation of the disc key")]
            public string Key { get; set; }

            [Option('l', "getkey-log", HelpText = "Path to a .getkey.log file")]
            public string GetKeyLog { get; set; }

            [Option('f', "key-file", HelpText = "Path to a redump .key file")]
            public string KeyFile { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and generate IRDs for all ISOs")]
            public bool Recurse { get; set; }
        }

        /// <summary>
        /// IRD or ISO information command
        /// </summary>
        [Verb("info", HelpText = "Print information from an IRD or ISO")]
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to an IRD or ISO file, or directory of IRD and/or ISO files")]
            public IEnumerable<string> InPath { get; set; }

            [Option('o', "output", HelpText = "Path to the text or json file to be created (will overwrite)")]
            public string OutPath { get; set; }

            [Option('j', "json", HelpText = "Print IRD or ISO information as a JSON object")]
            public bool Json { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and print information for all ISOs and IRDs")]
            public bool Recurse { get; set; }
        }

       /// <summary>
       /// IRD diff command
       /// </summary>
        [Verb("diff", HelpText = "Compare two IRDs and print their differences")]
        public class DiffOptions
        {
            [Value(0, Required = true, HelpText = "Path to the first IRD to compare against")]
            public string InPath1 { get; set; }

            [Value(1, Required = true, HelpText = "Path to the second IRD file to compare")]
            public string InPath2 { get; set; }

            [Option('o', "output", HelpText = "Path to the text or json file to be created (will overwrite)")]
            public string OutPath { get; set; }
        }

        #endregion

        #region Program

        /// <summary>
        /// Parse command line arguments
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            // Ensure console prints foreign characters properly
            Console.OutputEncoding = Encoding.UTF8;

            // Parse arguments
            var result = Parser.Default.ParseArguments<CreateOptions, InfoOptions, DiffOptions>(args).WithParsed(Run);
        }

        /// <summary>
        /// Parse arguments
        /// </summary>
        /// <param name="args">Command-line arguments</param>
        /// <exception cref="ArgumentException"></exception>
        private static void Run(object args)
        {
            switch (args)
            {
                // Process options from a `create` command
                case CreateOptions opt:

                    // Validate ISO paths
                    ArgumentNullException.ThrowIfNull(opt.ISOPath);

                    foreach (string isoPath in opt.ISOPath)
                    {
                        // Validate ISO path
                        ArgumentNullException.ThrowIfNull(isoPath);

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(isoPath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> isoFiles;
                            if (opt.Recurse)
                            {
                                if (isoPath == ".")
                                    Console.WriteLine($"Recursively searching for ISOs in current directory");
                                else
                                    Console.WriteLine($"Recursively searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (isoPath == ".")
                                    Console.WriteLine($"Searching for ISOs in current directory");
                                else
                                    Console.WriteLine($"Searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!isoFiles.Any())
                                Console.WriteLine("No ISOs found (ensure .iso extension)");

                            // Create an IRD file for all ISO files found
                            foreach (string file in isoFiles)
                                ISO2IRD(file);
                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(isoPath)) throw new ArgumentException("Not a valid file or directory");

                            // Save to given output path, if only 1 IRD is being created
                            if (opt.ISOPath.Count() == 1 && opt.IRDPath != null && opt.IRDPath != "")
                            {
                                string irdPath = ISO2IRD(isoPath, opt.IRDPath, opt.Key, opt.KeyFile, opt.GetKeyLog, opt.Layerbreak);
                                if (irdPath != null)
                                    Console.WriteLine($"IRD saved to {irdPath}");
                            }
                            else
                            {
                                string irdPath = ISO2IRD(isoPath, null, opt.Key, opt.KeyFile, opt.GetKeyLog, opt.Layerbreak);
                                if (irdPath != null)
                                    Console.WriteLine($"IRD saved to {irdPath}");
                            }
                        }
                    }

                    break;

                // Process options from an `info` command
                case InfoOptions opt:

                    // Validate required parameter
                    ArgumentNullException.ThrowIfNull(opt.InPath);

                    // Clear the output file path if it exists
                    if (opt.OutPath != null && opt.OutPath != "")
                        File.Delete(opt.OutPath);

                    foreach (string filePath in opt.InPath)
                    {
                        // Validate path
                        ArgumentNullException.ThrowIfNull(filePath);

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(filePath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> irdFiles;
                            IEnumerable<string> isoFiles;
                            if (opt.Recurse)
                            {
                                if (filePath == ".")
                                    Console.WriteLine($"Recursively searching for IRDs and ISOs in current directory...\n");
                                else
                                    Console.WriteLine($"Recursively searching for IRDs and ISOs in {filePath}...\n");
                                irdFiles = Directory.EnumerateFiles(filePath, "*.ird", SearchOption.AllDirectories);
                                isoFiles = Directory.EnumerateFiles(filePath, "*.iso", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (filePath == ".")
                                    Console.WriteLine($"Searching for IRDs and ISOs in current directory...\n");
                                else
                                    Console.WriteLine($"Searching for IRDs and ISOs in {filePath}...\n");
                                irdFiles = Directory.EnumerateFiles(filePath, "*.ird", SearchOption.TopDirectoryOnly);
                                isoFiles = Directory.EnumerateFiles(filePath, "*.iso", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!isoFiles.Any() && !irdFiles.Any())
                                Console.WriteLine("No IRDs or ISOs found (ensure .ird and .iso extensions)");

                            // Open JSON object
                            if (opt.Json)
                            {
                                if (opt.OutPath != null && opt.OutPath != "")
                                    File.AppendAllText(opt.OutPath, "{\n");
                                else
                                    Console.WriteLine('{');
                            }

                            // Print info from all IRDs
                            bool noISO = !isoFiles.Any();
                            string lastIRD = irdFiles.Last();
                            foreach (string file in irdFiles)
                            {
                                PrintInfo(file, opt.Json, (noISO && file.Equals(lastIRD)), opt.OutPath);
                            }


                            // Print info from all ISOs
                            string lastISO = isoFiles.Last();
                            foreach (string file in isoFiles)
                            {
                                try
                                {
                                    PrintISO(file, opt.Json, file.Equals(lastISO), opt.OutPath);
                                }
                                catch (InvalidFileSystemException)
                                {
                                    // Not a valid ISO file despite extension, assume file is an IRD
                                    if (!opt.Json)
                                        Console.WriteLine($"{file} is not a valid ISO file\n");
                                }
                            }

                            // Close JSON object
                            if (opt.Json)
                            {
                                if (opt.OutPath != null && opt.OutPath != "")
                                    File.AppendAllText(opt.OutPath, "}\n");
                                else
                                    Console.WriteLine('}');
                            }

                            if (opt.OutPath != null && opt.OutPath != "")
                                Console.WriteLine($"Info saved to {opt.OutPath}");
                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(filePath)) throw new ArgumentException($"{filePath} is not a valid file or directory");

                            // Print info from given file
                            PrintInfo(filePath, opt.Json, true, opt.OutPath);

                            if (opt.OutPath != null && opt.OutPath != "")
                                Console.WriteLine($"Info saved to {opt.OutPath}");
                        }
                    }

                    break;

                // Process options from a `diff` command
                case DiffOptions opt:

                    // Validate required parameter
                    ArgumentNullException.ThrowIfNull(opt.InPath1);
                    ArgumentNullException.ThrowIfNull(opt.InPath2);
                    if (!File.Exists(opt.InPath1)) throw new ArgumentException($"{opt.InPath1} is not a valid file or directory");
                    if (!File.Exists(opt.InPath2)) throw new ArgumentException($"{opt.InPath2} is not a valid file or directory");

                    // Clear the output file path if it exists
                    if (opt.OutPath != null && opt.OutPath != "")
                        File.Delete(opt.OutPath);

                    // Compare the two IRDs
                    PrintDiff(opt.InPath1, opt.InPath2, opt.OutPath);

                    if (opt.OutPath != null && opt.OutPath != "")
                        Console.WriteLine($"Diff saved to {opt.OutPath}");

                    break;

                // Unknown command
                default:
                    break;
            }
        }

        #endregion

        #region Functionality

        /// <summary>
        /// Prints info about a file
        /// </summary>
        /// <param name="inPath">File to retrieve info from</param>
        /// <param name="json">Whether to format output as JSON (true) or plain text (false)</param>
        /// <param name="outPath">File to output info to</param>
        public static void PrintInfo(string inPath, bool json, bool single = true, string outPath = null)
        {
            // Check if file is an ISO
            bool isISO = String.Compare(Path.GetExtension(inPath), ".iso", StringComparison.OrdinalIgnoreCase) == 0;
            if (isISO)
            {
                try
                {
                    PrintISO(inPath, json, single, outPath);
                    return;
                }
                catch (InvalidFileSystemException)
                {
                    // Not a valid ISO file despite extension, try open as IRD
                }
            }

            // Assume it is an IRD file
            try
            {
                if (json)
                {
                    IRD ird = IRD.Read(inPath);
                    if (outPath != null)
                        File.AppendAllText(outPath, $"\"{Path.GetFileName(inPath)}\": ");
                    else
                        Console.Write($"\"{Path.GetFileName(inPath)}\": ");
                    ird.PrintJson(outPath, single);
                }
                else
                    IRD.Read(inPath).Print(outPath, Path.GetFileName(inPath));

                if (json)
                    return;
                return;
            }
            catch (InvalidDataException)
            {
                // Not a valid IRD file despite extension, give up
                if (json)
                    return;
                if (isISO)
                    Console.WriteLine($"{inPath} is not a valid ISO file\n");
                else
                    Console.WriteLine($"{inPath} is not a valid IRD file\n");
            }
        }

        /// <summary>
        /// Print information about ISO file
        /// </summary>
        /// <param name="isoPath">Path to ISO file</param>
        /// <param name="json">Whether to format output as JSON (true) or plain text (false)</param>
        /// <param name="outPath">File to output info to</param>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidFileSystemException"></exception>
        public static void PrintISO(string isoPath, bool json, bool single = true, string outPath = null)
        {
            // Open ISO file for reading
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file stream
            if (!CDReader.Detect(fs))
                throw new InvalidFileSystemException($"{isoPath} is not a valid ISO file");
            // Create new ISO reader
            using CDReader reader = new(fs, true, true);

            // Write PS3_DISC.SFB info
            try
            {
                using DiscUtils.Streams.SparseStream s = reader.OpenFile("\\PS3_DISC.SFB", FileMode.Open, FileAccess.Read);
                PS3_DiscSFB ps3_DiscSFB = new(s);
                if (json)
                {
                    // Begin JSON object
                    if (json)
                    {
                        if (outPath != null)
                            File.AppendAllText(outPath, $"\"{Path.GetFileName(isoPath)}\": {{\n");
                        else
                            Console.WriteLine($"\"{Path.GetFileName(isoPath)}\": {{");
                    }

                    // Print PS3_DISC.SFB info
                    if (outPath != null)
                        File.AppendAllText(outPath, "\"PS3_DISC.SFB\": ");
                    else
                        Console.Write("\"PS3_DISC.SFB\": ");
                    ps3_DiscSFB.PrintJson(outPath);
                    if (outPath != null)
                        File.AppendAllText(outPath, ",\n");
                    else
                        Console.WriteLine(',');
                }
                else
                    ps3_DiscSFB.Print(outPath, Path.GetFileName(isoPath));
            }
            catch (FileNotFoundException)
            {
                if (!json)
                    Console.WriteLine($"{isoPath} is not a valid PS3 ISO file\n");
                return;
            }

            // Write PARAM.SFO info
            try
            {
                using DiscUtils.Streams.SparseStream s = reader.OpenFile("\\PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read);
                ParamSFO paramSFO = new(s);
                if (json)
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\"PARAM.SFO\": ");
                    else
                        Console.Write("\"PARAM.SFO\": ");
                    paramSFO.PrintJson(outPath);
                }
                else
                    paramSFO.Print(outPath, Path.GetFileName(isoPath));
            }
            catch (FileNotFoundException)
            {
                if (!json)
                    Console.WriteLine($"\\PS3_GAME\\PARAM.SFO not found in {isoPath}\n");
            }

            // End JSON object
            if (json)
            {
                if (single)
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\n}\n");
                    else
                        Console.WriteLine("\n}");
                }
                else
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\n},\n");
                    else
                        Console.WriteLine("\n},");
                }
            }
        }

        /// <summary>
        /// Prints the differences between two IRD files
        /// </summary>
        /// <param name="irdPath1">First IRD path to compare against</param>
        /// <param name="irdPath2">Second IRD path to compare against</param>
        /// <param name="outPath">File to write comparison to, null if print to Console</param>
        public static void PrintDiff(string irdPath1, string irdPath2, string outPath = null)
        {
            // Check they are different IRDs
            if (Path.GetFullPath(irdPath1) == Path.GetFullPath(irdPath2))
            {
                Console.WriteLine("Provide two different IRDs for a diff");
                return;
            }

            // Parse each IRD
            IRD IRD1 = IRD.Read(irdPath1);
            IRD IRD2 = IRD.Read(irdPath2);

            // Build a formatted diff
            StringBuilder printText = new();

            // Print any version difference
            if (IRD1.Version != IRD2.Version)
                printText.AppendLine($"Version: {IRD1.Version} vs {IRD2.Version}");

            // Print any title ID difference
            if (IRD1.TitleID != IRD2.TitleID)
                printText.AppendLine($"TitleID: {IRD1.TitleID} vs {IRD2.TitleID}");

            // Print any title difference
            if (IRD1.Title != IRD2.Title)
                printText.AppendLine($"Title: \"{IRD1.Title}\" vs \"{IRD2.Title}\"");

            // Print any system version difference
            if (IRD1.SystemVersion != IRD2.SystemVersion)
                printText.AppendLine($"PUP Version: {IRD1.SystemVersion} vs {IRD2.SystemVersion}");

            // Print any disc version difference
            if (IRD1.DiscVersion != IRD2.DiscVersion)
                printText.AppendLine($"Disc Version: {IRD1.DiscVersion} vs {IRD2.DiscVersion}");

            // Print any app version difference
            if (IRD1.AppVersion != IRD2.AppVersion)
                printText.AppendLine($"App Version: {IRD1.AppVersion} vs {IRD2.AppVersion}");

            // Un-gzip the headers to compare them
            byte[] header1 = Decompress(IRD1.Header);
            byte[] header2 = Decompress(IRD2.Header);

            // Print the difference in header length, if not 0
            if (header1.Length != header2.Length)
                printText.AppendLine($"Header Length: {header1.Length} vs {header2.Length}");

            // Print number of bytes that the headers differ by, if not 0
            int headerDiff;
            if (header1.Length < header2.Length)
                headerDiff = header2.Length - header1.Length + header1.Where((x, i) => x != header2[i]).Count();
            else
                headerDiff = header1.Length - header2.Length + header2.Where((x, i) => x != header1[i]).Count();
            if (headerDiff != 0)
                printText.AppendLine($"Header: Differs by {headerDiff} bytes");

            // Un-gzip the footers to compare them
            byte[] footer1 = Decompress(IRD1.Footer);
            byte[] footer2 = Decompress(IRD2.Footer);

            // Print the difference in footer length, if not 0
            if (footer1.Length != footer2.Length)
                printText.AppendLine($"Footer Length: {footer1.Length} vs {footer2.Length}");

            // Print number of bytes that the footers differ by, if not 0
            int footerDiff;
            if (footer1.Length < footer2.Length)
                footerDiff = footer2.Length - footer1.Length + footer1.Where((x, i) => x != footer2[i]).Count();
            else
                footerDiff = footer1.Length - footer2.Length + footer2.Where((x, i) => x != footer1[i]).Count();
            if (footerDiff != 0)
                printText.AppendLine($"Footer: Differs by {footerDiff} bytes");

            // Print the difference in number of regions, if not 0
            if (IRD1.RegionCount != IRD2.RegionCount)
                printText.AppendLine($"Region Count: {IRD1.RegionCount} vs {IRD2.RegionCount}");

            // Print any differences in region hashes
            int regionCount = IRD2.RegionCount < IRD1.RegionCount ? IRD2.RegionCount : IRD1.RegionCount;
            if (regionCount > IRD1.RegionHashes.Length)
                regionCount = IRD1.RegionHashes.Length;
            if (regionCount > IRD2.RegionHashes.Length)
                regionCount = IRD2.RegionHashes.Length;
            for (int i = 0; i < regionCount; i++)
            {
                if (!IRD1.RegionHashes[i].SequenceEqual(IRD2.RegionHashes[i]))
                    printText.AppendLine($"Region {i} Hash: {Convert.ToHexString(IRD1.RegionHashes[i])} vs {Convert.ToHexString(IRD2.RegionHashes[i])}");
            }

            // Print the difference in number of files, if not 0
            if (IRD1.FileCount != IRD2.FileCount)
                printText.AppendLine($"File Count: {IRD1.FileCount} vs {IRD2.FileCount}");

            // Print the mismatch file hashes, for each file offset at which they differ
            List<long> missingOffsets1 = [];
            List<long> missingOffsets2 = [];
            for (int i = 0; i < IRD1.FileKeys.Length; i++)
            {
                int j = Array.FindIndex(IRD2.FileKeys, element => element == IRD1.FileKeys[i]);
                if (j == -1)
                    missingOffsets2.Add(IRD1.FileKeys[i]);
                if (j != -1 && !IRD1.FileHashes[i].SequenceEqual(IRD2.FileHashes[j]))
                    printText.AppendLine($"File Hash at Offset {IRD1.FileKeys[i]}: {Convert.ToHexString(IRD1.FileHashes[i])} vs {Convert.ToHexString(IRD2.FileHashes[j])}");
            }
            for (int i = 0; i < IRD2.FileKeys.Length; i++)
            {
                int j = Array.FindIndex(IRD1.FileKeys, element => element == IRD2.FileKeys[i]);
                if (j == -1)
                    missingOffsets1.Add(IRD2.FileKeys[i]);
            }
            // Print the file offsets that differ
            printText.AppendLine($"File Offsets not Present in {irdPath1}: {string.Join(", ", missingOffsets1)}");
            printText.AppendLine($"File Offsets not Present in {irdPath2}: {string.Join(", ", missingOffsets2)}");

            // Print any extra config data difference
            if (IRD1.ExtraConfig != IRD2.ExtraConfig)
                printText.AppendLine($"Extra Config: {IRD1.ExtraConfig:X4} vs {IRD2.ExtraConfig:X4}");

            // Print any attachments data difference
            if (IRD1.Attachments != IRD2.Attachments)
                printText.AppendLine($"Attachments: {IRD1.Attachments:X4} vs {IRD2.Attachments:X4}");

            // Print any unique ID difference
            if (IRD1.UID != IRD2.UID)
                printText.AppendLine($"Unique ID: {IRD1.UID:X8} vs {IRD2.UID:X8}");

            // Print any data 1 key difference
            if (!IRD1.Data1Key.SequenceEqual(IRD2.Data1Key))
                printText.AppendLine($"Data 1 Key: {Convert.ToHexString(IRD1.Data1Key)} vs {Convert.ToHexString(IRD2.Data1Key)}");

            // Print any data 2 key difference
            if (!IRD1.Data2Key.SequenceEqual(IRD2.Data2Key))
                printText.AppendLine($"Data 2 Key: {Convert.ToHexString(IRD1.Data2Key)} vs {Convert.ToHexString(IRD2.Data2Key)}");

            // Print any PIC difference
            if (!IRD1.PIC.SequenceEqual(IRD2.PIC))
                printText.AppendLine($"PIC: {Convert.ToHexString(IRD1.PIC)} vs {Convert.ToHexString(IRD2.PIC)}");

            // Write formatted string to file if output path provided, otherwise to console
            if (outPath != null)
                File.AppendAllText(outPath, printText.ToString());
            else
                Console.WriteLine(printText.ToString());
        }

        /// <summary>
        /// Creates an IRD file from an ISO file
        /// </summary>
        /// <param name="isoPath">Path to an ISO file</param>
        /// <param name="irdPath">Path to IRD file to be created (optional)</param>
        /// <param name="hexKey">Hex string disc key</param>
        /// <param name="keyPath">Disc key file (overridden by hex string if present)</param>
        /// <param name="getKeyLog">GetKey log file (overridden by disc key or key file if present)</param>
        /// <param name="layerbreak">Layerbreak value of disc</param>
        public static string ISO2IRD(string isoPath, string irdPath = null, string hexKey = null, string keyPath = null, string getKeyLog = null, long? layerbreak = null)
        {
            // Check file exists
            FileInfo iso = new(isoPath);
            if (!iso.Exists)
            {
                Console.WriteLine($"{nameof(isoPath)} is not a valid file or directory");
                return null;
            }

            // Determine IRD path if none given
            irdPath ??= Path.ChangeExtension(isoPath, ".ird");

            // Create new reproducible redump-style IRD with a given hex key
            if (hexKey != null)
            {
                try
                {
                    // Get disc key from hex string
                    byte[] discKey = Convert.FromHexString(hexKey);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(hexKey);

                    Console.WriteLine($"Creating {irdPath} with Key: {hexKey}");
                    IRD ird1 = new ReIRD(isoPath, discKey, layerbreak);
                    ird1.Write(irdPath);
                    ird1.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine($"{hexKey} is not a valid key, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given key file
            if (keyPath != null)
            {
                // Read key from .key file
                byte[] discKey = File.ReadAllBytes(keyPath);
                try
                {
                    IRD ird2 = new ReIRD(isoPath, discKey, layerbreak);
                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    ird2.Write(irdPath);
                    ird2.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine($"{Convert.ToHexString(discKey)} is not a valid key, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given GetKey log
            if (getKeyLog != null)
            {
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {getKeyLog}");
                    IRD ird3 = new ReIRD(isoPath, getKeyLog);
                    ird3.Write(irdPath);
                    ird3.Print();
                    return irdPath;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try search for .key file
            string keyfilePath = Path.ChangeExtension(isoPath, ".key");
            FileInfo keyFile = new(keyfilePath);
            if (keyFile.Exists)
            {
                // Found .key file, try use it
                try
                {
                    // Read key from .key file
                    byte[] discKey = File.ReadAllBytes(keyfilePath);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(keyfilePath);

                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    IRD ird2 = new ReIRD(isoPath, discKey, layerbreak);
                    ird2.Write(irdPath);
                    ird2.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Given key file not valid, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try search for .getkey.log file
            string logfilePath = Path.ChangeExtension(isoPath, ".getkey.log");
            FileInfo logfile = new(logfilePath);
            if (logfile.Exists)
            {
                // Found .getkey.log file, check it is valid
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {logfilePath}");
                    IRD ird3 = new ReIRD(isoPath, logfilePath);
                    ird3.Write(irdPath);
                    ird3.Print();
                    return irdPath;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try get key from redump.org
            Console.WriteLine("No key provided... Searching for key on redump.org");

            // Compute CRC32 hash
            byte[] crc32;
            using (FileStream fs = File.OpenRead(isoPath))
            {
                Crc32 hasher = new();
                hasher.Append(fs);
                crc32 = hasher.GetCurrentHash();
                // Change endianness
                Array.Reverse(crc32);
            }
            string crc32_hash = Convert.ToHexString(crc32).ToLower();

            // Search for ISO on redump.org
            RedumpHttpClient redump = new();
            List<int> ids = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + crc32_hash).ConfigureAwait(false).GetAwaiter().GetResult();
            int id;
            if (ids.Count == 0)
            {
                Console.WriteLine("ISO not found in redump, cannot automatically retreive key");
                return null;
            }
            else if (ids.Count > 1)
            {
                // Compute SHA1 hash
                byte[] sha1;
                using (FileStream fs = File.OpenRead(isoPath))
                {
                    SHA1 hasher = SHA1.Create();
                    sha1 = hasher.ComputeHash(fs);
                }
                string sha1_hash = Convert.ToHexString(sha1).ToLower();

                // Search redump.org for SHA1 hash
                List<int> ids2 = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + sha1_hash).ConfigureAwait(false).GetAwaiter().GetResult();
                if (ids2.Count == 0)
                {
                    Console.WriteLine("ISO not found in redump, cannot automatically retreive key");
                    return null;
                }
                else if (ids2.Count > 1)
                {
                    Console.WriteLine("Cannot automatically get key from redump. Please search redump.org and run again with -k");
                    return null;
                }
                id = ids2[0];
            }
            else
            {
                id = ids[0];
            }

            // Download key file from redump.org
            byte[] key = redump.GetByteArrayAsync($"http://redump.org/disc/{id}/key").ConfigureAwait(false).GetAwaiter().GetResult();
            if (key.Length != 16)
            {
                Console.WriteLine("Invalid key obtained from redump");
            }

            // Create IRD with key from redump
            Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(key)}");
            IRD ird = new ReIRD(isoPath, key, layerbreak);
            ird.Write(irdPath);
            ird.Print();
            return irdPath;
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Decompress a gzipped byte array
        /// </summary>
        /// <param name="data">Gzipped byte array</param>
        /// <returns>Un-gzipped byte array</returns>
        static byte[] Decompress(byte[] data)
        {
            using var compressedStream = new MemoryStream(data);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            return resultStream.ToArray();
        }

        #endregion
    }
}
