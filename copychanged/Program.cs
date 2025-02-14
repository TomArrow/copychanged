using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace copychanged
{
    // TODO when running from .bat, we get no printing of speed 
    // and HDD usage is 100% however very low throughput (few MB per s)
    // but in git bash it runs as expected with high throughput. why?

    // TODO Preserve date modified and maybe date created even?
    // TODO folder date modified: restore ata the end of copying to that folder.

    // TODO copy empty folders?


    // IMPORTANT: Path concept
    // Because Windows acts strange with paths and Path.GetFullPath etc., and we want to be able to access all kinds of folder/file names (trailing/leading zeros and dots at end),
    // we need a solid concept. We will ignore any kind of case sensitiveness, that will get too complicated.
    // For consistency, we are going to make one compromise: The original paths will be normalized as per usual to get good base paths to build off of.
    // From there, we will turn them absolute via \\?\ prefix. (how to handle linux?)
    // GetFiles and GetDirectories will give us \\?\ paths in return as well
    // We will avoid using Path.GetRelativePath and instead do our own variant that uses substrings (which will hopefully work because we sanitized the original paths already and they are in a semi-consistent form)
    // 
    class FileToCopy {
        public string from { get; set; }
        public string to { get; set; }
        public UInt64 size { get; set; }
        public UInt64 sizeTarget { get; set; }
        public bool existsAndDifferent { get; set; }
        public bool existsAndIdentical { get; set; }
        public bool system { get; set; }
    }

    class PostAnalysisState
    {
        public string folder1 { get; set; }  = null;
        public string folder2 { get; set; }  = null;
        public UInt64 totalCompared { get; set; } = 0;
        public UInt64 totalLookedAt { get; set; } = 0;
        public UInt64 systemCount { get; set; } = 0;
        public double totalSecondsTaken { get; set; } = 0;
        public List<FileToCopy> filesToFix { get; set; } = new List<FileToCopy>();
        public List<FileToCopy> filesToCopy { get; set; }  = new List<FileToCopy>();
        public List<FileToCopy> filesConfirmed { get; set; }  = new List<FileToCopy>();
        public List<string> errors { get; set; }  = new List<string>();

    }


    class Program
    {
        static void PrintHelp()
        {

            Console.WriteLine("Usage: copychanged [-n,--donew] [-c,--dochanged] referencefolder destinationfolder");
            Console.WriteLine("Optional param meaning:");
            Console.WriteLine("-n,--donew           Copy files that didn't exist yet.");
            Console.WriteLine("-c,--dochanged       Copy files that are different (overwrite!).");
            Console.WriteLine("-l,--log             Log what was done into _copychanged_copy.log and _copychanged_fix.log.");
            Console.WriteLine("-v,--verbose         Logging will include list of files that need to be copied (not shown by default to avoid spam). By default, _copychanged_copy.log only contains any errors or retries during copying and the count of files.");
            Console.WriteLine("-s,--save            Saves a serialized list of files that need to be processed in _copychanged_list.json");
            Console.WriteLine("-l,--load            Loads a serialized list of files that need to be processed from _copychanged_list.json");
            Console.WriteLine("-d,--delorig         Deletes the file in the reference folder once its authenticity in the target folder is verified");
            Console.WriteLine("-y,--dosystem        Handle files inside system folders (individual files that have the System attribute are always handled)");
            Console.WriteLine("-u,--nodatecreated   Don't preserve Date Created (default does preserve it). Date Modified is always preserved regardless.");
            Console.WriteLine("-e,--exclude         Followed up by a path, this lets you exclude a path (folder, not a file) from being considered");
            Console.WriteLine("By default you are only told the amount of needed copy operations, no copying happens.");
            Console.WriteLine("Copy operations are ALWAYS verified and will be repeated if a difference is found.");
            Console.WriteLine("--save and --load work INDEPENDENTLY of --donew and --dochanged. You can do both or either.");
        }

        static JsonSerializerOptions jsonOpts = new JsonSerializerOptions() {NumberHandling= System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals| System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString, WriteIndented = true };

        static PostAnalysisState RunCompare(string folder1, string folder2,string[] excludePaths)
        {
            PostAnalysisState state = new PostAnalysisState();
            state.folder1 = folder1;// SafeGetFullPath(folder1);
            state.folder2 = folder2;// SafeGetFullPath(folder2);

            state.filesToCopy = new List<FileToCopy>();
            state.filesToFix = new List<FileToCopy>();

            state.totalCompared = 0;

            Console.Write("\nStarting file compare...");

            // have a few empty lines to print our live updates to
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();

            Stopwatch sw = new Stopwatch();

            sw.Start();

            DoFolderRecursive(state.folder1, state.folder2, state.folder1, state, sw,false, excludePaths);

            sw.Stop();

            state.totalSecondsTaken = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;


            try
            {
                Console.SetCursorPosition(Console.BufferWidth-1, Console.BufferHeight-1);
            } catch (Exception ex)
            {
                Console.WriteLine($"Error resetting cursor position: {ex.ToString()}");
            }
            Console.WriteLine();
            Console.WriteLine();

            return state;
        }


        static bool isAnalyzing = true;
        static bool cancelAnalysis = false;
        static bool cancelExecute = false;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                PrintHelp();
                return;
            }

            Console.CancelKeyPress += Console_CancelKeyPress;

            bool doNew = false;
            bool doChanged = false;
            bool doLog = false;
            bool verboseLog = false;
            bool doSave = false;
            bool doLoad = false;
            bool doDelOrig = false;
            bool doSystem = false;
            bool ignoreDateCreated = false;

            List<string> excludePaths = new List<string>();

            string folder1 = null;
            string folder2 = null;

            bool nextIsExcludePath = false;

            foreach(string arg in args)
            {
                if (nextIsExcludePath)
                {
                    string toAdd = NormalizePathEnding(SemiSafeGetFullPath(arg));
                    //while(toAdd.Length > 0 && (toAdd.EndsWith("\\") || toAdd.EndsWith("/")))
                    //{
                    //    toAdd = toAdd.Substring(0, toAdd.Length - 1);
                    //}
                    //toAdd += Path.DirectorySeparatorChar;
                    excludePaths.Add(toAdd);
                    nextIsExcludePath = false;
                }
                else if (arg.Equals("--donew",StringComparison.OrdinalIgnoreCase) || arg.Equals("-n", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--donew/-n option detected. Will copy files that don't exist yet.");
                    doNew = true;
                }
                else if (arg.Equals("--delorig", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--delorig/-d option detected. Will delete files from reference folder once their authenticity in target folder is confirmed.");
                    doDelOrig = true;
                }
                else if (arg.Equals("--save",StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--save/-s option detected. Will save analysis state to _copychanged_list.json");
                    doSave = true;
                } 
                else if (arg.Equals("--load",StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--load/-l option detected. Will load analysis state from _copychanged_list.json");
                    doLoad = true;
                } 
                else if (arg.Equals("--dochanged",StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--dochanged/-c option detected. Will overwrite files that changed.");
                    doChanged = true;
                } 
                else if (arg.Equals("--log",StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--log/-l option detected. Will write logs into _copychanged_copy.log and _copychanged_fix.log.");
                    doLog = true;
                } 
                else if (arg.Equals("--verbose",StringComparison.OrdinalIgnoreCase) || arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--verbose/-v option detected. Will write more details into _copychanged_copy.log (if --log is specified).");
                    verboseLog = true;
                } 
                else if (arg.Equals("--dosystem",StringComparison.OrdinalIgnoreCase) || arg.Equals("-y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--dosystem/-y option detected. Will handle files inside system folders (default: no).");
                    doSystem = true;
                } 
                else if (arg.Equals("--nodatecreated", StringComparison.OrdinalIgnoreCase) || arg.Equals("-u", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--nodatecreated/-u option detected. Will NOT preserve Date Created when copying/updating files");
                    ignoreDateCreated = true;
                }
                else if (arg.Equals("--exclude", StringComparison.OrdinalIgnoreCase) || arg.Equals("-e", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--exclude/-e option detected. Will ignore files in following path.");
                    nextIsExcludePath = true;
                } else if(folder1 == null)
                {
                    folder1 = arg;
                } else if(folder2 == null)
                {
                    folder2 = arg;
                }
                else
                {
                    Console.WriteLine($"Too many arguments/unrecognized argument: {arg}");
                    PrintHelp();
                    return;
                }
            }

            folder1 = SemiSafeGetFullPath(folder1);
            folder2 = SemiSafeGetFullPath(folder2);

            //while (!string.IsNullOrWhiteSpace(folder1) && (folder1.EndsWith("\\") || folder1.EndsWith("/")))
            //{
            //    folder1 = folder1.Substring(0, folder1.Length - 1);
            //}
            
            //while (!string.IsNullOrWhiteSpace(folder2) && (folder2.EndsWith("\\") || folder2.EndsWith("/")))
            //{
            //    folder2 = folder2.Substring(0, folder2.Length - 1);
            //}

            PostAnalysisState state = null;

            if (!doLoad)
            {
                if(string.IsNullOrWhiteSpace(folder1) || string.IsNullOrWhiteSpace(folder2))
                {
                    Console.WriteLine($"Not loading list, and didn't specify two folders.");
                    PrintHelp();
                    return;
                }
                try
                {
                    state = RunCompare(folder1, folder2, excludePaths.ToArray());
                } catch(Exception ex)
                {
                    Console.WriteLine($"Error comparing: {ex.ToString()}. Trying to salvage.");
                }

            }
            else
            {
                if (!SafeFileExists("_copychanged_list.json"))
                {
                    Console.WriteLine($"Can't load list, _copychanged_list.json not found.");
                    PrintHelp();
                    return;
                }
                if(!string.IsNullOrWhiteSpace(folder1) || !string.IsNullOrWhiteSpace(folder2))
                {
                    Console.WriteLine($"Warning: Loading from _copychanged_list.json, but at least one folder was specified. Ignoring folder(s).");
                }
                try
                {
                    string json = File.ReadAllText("_copychanged_list.json");
                    state = JsonSerializer.Deserialize<PostAnalysisState>(json, jsonOpts);
                } catch(Exception ex)
                {
                    Console.WriteLine($"Error loading list from _copychanged_list.json: {ex.ToString()}");
                    return;
                }
            }

            isAnalyzing = false;

            if (doSave)
            {
                string json = JsonSerializer.Serialize(state, jsonOpts);
                if (SafeFileExists("_copychanged_list.json")) // make up to 2 backups just in case. shitty way of doing it tho xd.
                {
                    if (SafeFileExists("_copychanged_list.json.bak"))
                    {
                        if (SafeFileExists("_copychanged_list.json.finalbak"))
                        {
                            SafeFileDelete("_copychanged_list.json.finalbak");
                        }
                        SafeFileMove("_copychanged_list.json.bak", "_copychanged_list.json.finalbak");
                    }
                    SafeFileMove("_copychanged_list.json", "_copychanged_list.json.bak");
                }
                File.WriteAllText("_copychanged_list.json",json);
            }



            Console.WriteLine();
            if(state.systemCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            Console.WriteLine($"{state.systemCount} total reference files checked are in system folders. Handling system files: {doSystem}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{state.filesToCopy.Count} files don't exist yet.");
            Console.WriteLine($"{state.filesToFix.Count} files are different:");
            foreach(FileToCopy fileToFix in state.filesToFix)
            {
                Console.WriteLine($"{fileToFix.to}");
            }

            DateTime now = DateTime.Now;

            if (doLog)
            {
                StringBuilder sb = new StringBuilder();
                string header = $"[{now.ToString("yyyy-MM-dd HH:mm:ss")}]";

                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(header);
                sb.AppendLine();
                sb.AppendLine("Files to be fixed:");
                UInt64 totalCount = (UInt64)state.filesToFix.Count;
                UInt64 totalSize = 0;
                UInt64 totalSizeDest = 0;
                if (state.filesToFix.Count > 0)
                {
                    foreach (FileToCopy fileToFix in state.filesToFix)
                    {
                        totalSize += fileToFix.size;
                        totalSizeDest += fileToFix.sizeTarget;
                        string size1 = fileToFix.size.ToString("#,##0");
                        string size2 = fileToFix.sizeTarget.ToString("#,##0");
                        string append = "";
                        if (fileToFix.system)
                        {
                            append = " (SYSTEM FOLDER)";
                        }
                        sb.AppendLine($"{fileToFix.from} ({size1} bytes) --> {fileToFix.to} ({size2} bytes){append}");
                    }
                }
                else
                {
                    sb.AppendLine("None.");
                }
                string sizeTotal = totalSize.ToString("#,##0");
                string sizeTotalDest = totalSizeDest.ToString("#,##0");
                sb.AppendLine();
                sb.AppendLine($"Total: {totalCount} files totaling {sizeTotal} bytes ({sizeTotalDest} bytes of target files at this time).");
                sb.AppendLine();



                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(header);
                sb.AppendLine();
                sb.AppendLine("Confirmed files that could/will be deleted:");
                totalCount = (UInt64)state.filesConfirmed.Count;
                totalSize = 0;
                totalSizeDest = 0;
                if (state.filesConfirmed.Count > 0)
                {
                    foreach (FileToCopy fileConfirmed in state.filesConfirmed)
                    {
                        totalSize += fileConfirmed.size;
                        totalSizeDest += fileConfirmed.sizeTarget;
                        if (verboseLog || fileConfirmed.system)
                        {
                            string size1 = fileConfirmed.size.ToString("#,##0");
                            string size2 = fileConfirmed.sizeTarget.ToString("#,##0");
                            string append = "";
                            if (fileConfirmed.system)
                            {
                                append = " (SYSTEM FOLDER)";
                            }
                            sb.AppendLine($"{fileConfirmed.from} ({size1} bytes) --> {fileConfirmed.to} ({size2} bytes){append}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("None.");
                }
                sizeTotal = totalSize.ToString("#,##0");
                sizeTotalDest = totalSizeDest.ToString("#,##0");
                sb.AppendLine();
                sb.AppendLine($"Total: {totalCount} files totaling {sizeTotal} bytes ({sizeTotalDest} bytes of target files at this time).");
                sb.AppendLine();


                File.AppendAllText("_copychanged_fix.log", sb.ToString());
                sb.Clear();

                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine(header);
                sb.AppendLine();
                sb.AppendLine("Files to be copied:");
                totalCount = (UInt64)state.filesToCopy.Count;
                totalSize = 0;
                if (state.filesToCopy.Count > 0)
                {
                    foreach (FileToCopy fileToCopy in state.filesToCopy)
                    {
                        totalSize += fileToCopy.size;
                        if (verboseLog || fileToCopy.system)
                        {
                            string size1 = fileToCopy.size.ToString("#,##0");
                            string append = "";
                            if (fileToCopy.system)
                            {
                                append = " (SYSTEM FOLDER)";
                            }
                            sb.AppendLine($"{fileToCopy.from} ({size1} bytes) --> {fileToCopy.to} (doesn't exist){append}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("None.");
                }
                sizeTotal = totalSize.ToString("#,##0");
                sb.AppendLine();
                sb.AppendLine($"Total: {totalCount} files totaling {sizeTotal} bytes.");
                sb.AppendLine();
                File.AppendAllText("_copychanged_copy.log", sb.ToString());
                sb.Clear();
            }

            CancellationTokenSource cts = new CancellationTokenSource();

            if(state.errors.Count > 0)
            {
                Console.WriteLine($"Encountered {state.errors.Count} errors:\n");
                foreach(string error in state.errors)
                {
                    Console.WriteLine(error);
                    Console.WriteLine();
                }
                if (doLog)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Encountered {state.errors.Count} errors:\n");
                    foreach (string error in state.errors)
                    {
                        sb.AppendLine(error);
                        sb.AppendLine();
                    }
                    File.AppendAllText("_copychanged_copy.log", sb.ToString()); // TODO which file should it go to?
                    sb.Clear();
                }
            }

            Int64 totalSuccess = 0;
            Int64 totalFail = 0;
            if (doDelOrig && state.filesConfirmed.Count > 0)
            {
                if (cancelExecute)
                {
                    Console.WriteLine("Canceling execution...");
                    return;
                }
                Console.WriteLine("Deleting confirmed files now");
                foreach (FileToCopy confirmedFile in state.filesConfirmed)
                {
                    if (cancelExecute)
                    {
                        Console.WriteLine("Canceling execution...");
                        return;
                    }
                    if (confirmedFile.system && !doSystem)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write($"Skipping deletion of SYSTEM FILE {confirmedFile.from}.");
                        Console.ForegroundColor = ConsoleColor.White;
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_fix.log", $"{confirmedFile.from} can't be deleted (identical with target file). SYSTEM FOLDER!\n");
                        }
                        continue;
                    }
                    Console.Write($"{confirmedFile.to}...");
                    if (!confirmedFile.existsAndIdentical)
                    {
                        Console.Write("wtf, not actually identical? WHAT? aborting.");
                        continue;
                    }
                    try
                    {
                        if (SafeFileExists(confirmedFile.from))
                        {
                            if (cancelExecute)
                            {
                                Console.WriteLine("Canceling execution...");
                                return;
                            }
                            SafeFileDelete(confirmedFile.from);
                            totalSuccess++;
                            Console.Write($"Successfully deleted original.");
                            if (doLog && verboseLog)
                            {
                                File.AppendAllText("_copychanged_fix.log", $"{confirmedFile.from} successfully deleted (identical with target file).\n");
                            }
                        }
                        else
                        {
                            totalFail++;
                            Console.Write($"WEIRD, original file gone? Can't delete then.");
                            if (doLog)
                            {
                                File.AppendAllText("_copychanged_fix.log", $"{confirmedFile.from} can't be deleted (identical with target file). Original file is gone!\n");
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        totalFail++;
                        Console.Write($"Error happened when trying to delete original. {ex.ToString()}");
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_fix.log", $"{confirmedFile.from} failed to delete:\n{ex.ToString()}\n");
                        }
                    }
                    Console.WriteLine();
                }
            }
            if (doLog)
            {
                File.AppendAllText("_copychanged_fix.log", $"\n{totalSuccess} original files successfully deleted after confirmation of identity with target files, {totalFail} failed.\n");
            }
            totalSuccess = 0;
            totalFail = 0;
            if (doChanged && state.filesToFix.Count > 0)
            {
                Console.WriteLine("Overwriting changed files now"); 
                foreach (FileToCopy fileToFix in state.filesToFix)
                {
                    if (cancelExecute)
                    {
                        Console.WriteLine("Canceling execution...");
                        return;
                    }
                    if (fileToFix.system && !doSystem)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write($"Skipping fixing of SYSTEM FILE {fileToFix.from}.");
                        Console.ForegroundColor = ConsoleColor.White;
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_fix.log", $"{fileToFix.from} can't be fixed. SYSTEM FOLDER!\n");
                        }
                        continue;
                    }
                    Console.Write($"{fileToFix.to}...");
                    int currentAttempt = 0;
                    try
                    {
                        string folder = SafeGetDirName(fileToFix.to);
                        if (!SafeDirExists(folder))
                        {
                            Console.Write($"creating destination folder, WTF?!...");
                            //Directory.CreateDirectory(folder);
                            MakeFolderWithDate(SafeGetDirName(fileToFix.from), folder, !ignoreDateCreated);
                        }

                        bool different = true;
                        while (different)
                        {
                            if (SafeFileExists(fileToFix.to))
                            {
                                if (cancelExecute)
                                {
                                    Console.WriteLine("Canceling execution...");
                                    return;
                                }
                                SafeFileDelete(fileToFix.to);
                            } else
                            {
                                Console.Write($"WEIRD, target file gone? Copying...");
                            }
                            if (cancelExecute)
                            {
                                Console.WriteLine("Canceling execution...");
                                return;
                            }

                            CopyWithDate(fileToFix.from, fileToFix.to, !ignoreDateCreated);
                            Console.Write($"Copied, verifying...");
                            if (cancelExecute)
                            {
                                Console.WriteLine("Canceling execution...");
                                return;
                            }
                            different = !FilesAreSame(fileToFix.from, fileToFix.to,cts.Token);
                            if (different)
                            {
                                currentAttempt++;
                                Console.Write($"Overwritten file is STILL different. Retry {currentAttempt}....");
                            }
                            else
                            {
                                totalSuccess++;
                                Console.Write($"File successfully updated.");
                                if (doDelOrig)
                                {
                                    if (cancelExecute)
                                    {
                                        Console.WriteLine("Canceling execution...");
                                        return;
                                    }
                                    SafeFileDelete(fileToFix.from);
                                    Console.Write($" Original deleted.");
                                    if (doLog && (verboseLog || currentAttempt > 0))
                                    {
                                        File.AppendAllText("_copychanged_fix.log", $"{fileToFix.to} successfully updated, and original deleted. ({currentAttempt} retries)\n");
                                    }
                                }
                                else
                                {
                                    if (doLog && (verboseLog || currentAttempt > 0))
                                    {
                                        File.AppendAllText("_copychanged_fix.log", $"{fileToFix.to} successfully updated ({currentAttempt} retries)\n");
                                    }
                                }
                            }
                        }
                    } catch(Exception ex)
                    {
                        totalFail++;
                        Console.Write($"Error happened when trying to overwrite. {ex.ToString()}");
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_fix.log", $"{fileToFix.to} failed to update ({currentAttempt} retries):\n{ex.ToString()}\n");
                        }
                    }
                    Console.WriteLine();
                }
            }
            if (doLog)
            {
                File.AppendAllText("_copychanged_fix.log", $"\n{totalSuccess} files successfully updated, {totalFail} failed.\n");
            }

            totalSuccess = 0;
            totalFail = 0;
            if (doNew && state.filesToCopy.Count > 0)
            {
                Console.WriteLine("Copying new files now"); 
                foreach (FileToCopy fileToCopy in state.filesToCopy)
                {
                    if (cancelExecute)
                    {
                        Console.WriteLine("Canceling execution...");
                        return;
                    }
                    if (fileToCopy.system && !doSystem)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write($"Skipping copying of SYSTEM FILE {fileToCopy.from}.");
                        Console.ForegroundColor = ConsoleColor.White;
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_fix.log", $"{fileToCopy.from} can't be copied. SYSTEM FOLDER!\n");
                        }
                        continue;
                    }
                    Console.Write($"{fileToCopy.to}...");
                    int currentAttempt = 0;
                    try
                    {
                        string folder = SafeGetDirName(fileToCopy.to);
                        if (!SafeDirExists(folder))
                        {
                            Console.Write($"creating destination folder...");
                            //Directory.CreateDirectory(folder);
                            MakeFolderWithDate(SafeGetDirName(fileToCopy.from),folder,!ignoreDateCreated);
                        }

                        bool different = true;
                        while (different)
                        {
                            if (SafeFileExists(fileToCopy.to))
                            {
                                if (currentAttempt == 0)
                                {
                                    Console.Write($"wtf already exists... deleting...");
                                }
                                if (cancelExecute)
                                {
                                    Console.WriteLine("Canceling execution...");
                                    return;
                                }
                                SafeFileDelete(fileToCopy.to);
                            } else if (currentAttempt != 0)
                            {
                                Console.Write($"WEIRD, target file gone? Copying again...");
                            }
                            if (cancelExecute)
                            {
                                Console.WriteLine("Canceling execution...");
                                return;
                            }
                            CopyWithDate(fileToCopy.from, fileToCopy.to,!ignoreDateCreated);
                            Console.Write($"Copied, verifying...");
                            if (cancelExecute)
                            {
                                Console.WriteLine("Canceling execution...");
                                return;
                            }
                            different = !FilesAreSame(fileToCopy.from, fileToCopy.to,cts.Token);
                            if (different)
                            {
                                currentAttempt++;
                                Console.Write($"Copied file is STILL different. Retry {currentAttempt}....");
                            }
                            else
                            {
                                totalSuccess++;
                                Console.Write($"File successfully copied.");
                                if (doDelOrig)
                                {
                                    if (cancelExecute)
                                    {
                                        Console.WriteLine("Canceling execution...");
                                        return;
                                    }
                                    SafeFileDelete(fileToCopy.from);
                                    Console.Write($" Original deleted.");
                                    if (doLog && (verboseLog || currentAttempt > 0))
                                    {
                                        File.AppendAllText("_copychanged_copy.log", $"{fileToCopy.to} successfully copied, and original deleted. ({currentAttempt} retries)\n");
                                    }
                                }
                                else
                                {
                                    if (doLog && (verboseLog || currentAttempt > 0))
                                    {
                                        File.AppendAllText("_copychanged_copy.log", $"{fileToCopy.to} successfully copied ({currentAttempt} retries)\n");
                                    }
                                }
                            }
                        }
                    } catch(Exception ex)
                    {
                        totalFail++;
                        if (doLog)
                        {
                            File.AppendAllText("_copychanged_copy.log", $"{fileToCopy.to} failed to copy ({currentAttempt} retries):\n{ex.ToString()}\n");
                        }
                        Console.Write($"Error happened when trying to copy file. {ex.ToString()}");
                    }
                    Console.WriteLine();
                }
            }
            
            
            if (doLog)
            {
                File.AppendAllText("_copychanged_copy.log", $"\n{totalSuccess} files successfully copied, {totalFail} failed.\n");
            }


            //for (int i = 0; i < 10; i++)
            //{
            //    try
            //    {
            //        string file1 = args[0];
            //        string file2 = args[1];
            //        CancellationTokenSource cts = new CancellationTokenSource();

            //        Stopwatch sw = new Stopwatch();
            //        sw.Restart();
            //        bool same;
            //        using(FileStream fs1 = File.OpenRead(file1))
            //        {
            //            using(FileStream fs2 = File.OpenRead(file2))
            //            {
            //                same = VectorizedComparer.Same(fs1, fs2, cts.Token, default, true);
            //            }
            //        }
            //        sw.Stop();
            //        double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
            //        Console.WriteLine($"Comparison took {time} seconds. Same: {same}");
            //    }
            //    catch (Exception e)
            //    {
            //        Console.WriteLine(e.ToString());
            //    }
            //}
            //Console.ReadKey();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (isAnalyzing)
            {
                cancelAnalysis = true;
            }
            else
            {
                cancelExecute = true;
            }
            e.Cancel = true;
        }

        static bool FilesAreSame(string file1, string file2, CancellationToken ct)
        {
            using (FileStream fs1 = File.OpenRead(file1))
            {
                using (FileStream fs2 = File.OpenRead(file2))
                {
                    return VectorizedComparer.Same(fs1, fs2, ct, default, true);
                }
            }
        }

        static void PrintUpdate(Stopwatch sw, string currentFolder, string destinationFolder, string currentFile, PostAnalysisState state, bool system)
        {

            double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;

            double bytesPerSecond = state.totalCompared / time;
            string bpsString = bytesPerSecond.ToString("#,##0.00");
            string totalAmount = state.totalCompared.ToString("#,##0");
            string totalAmountLookedAt = state.totalLookedAt.ToString("#,##0");
            try
            {
                int startLine = Math.Max(0, Console.BufferHeight - 7);
                int endLine = Console.BufferHeight;
                for(int line = startLine; line < endLine; line++)
                {
                    // clear the line
                    Console.SetCursorPosition(0, line);
                    if (Console.BufferWidth <= 1) continue;
                    Console.Write(new string(' ',Console.BufferWidth-1));
                    string toPrint = "";
                    string prefix = "";
                    bool cutStringReverse = true;
                    switch (endLine - line) { // reverse order...
                        case 1:
                            prefix = "Speed avg: ";
                            toPrint = $"\r{bpsString} bytes per second";
                            cutStringReverse = false;
                            break;
                        case 2:
                            prefix = "Bytes cmp: ";
                            toPrint = $"{totalAmount} bytes";
                            cutStringReverse = false;
                            break;
                        case 3:
                            prefix = "Bytes:     ";
                            toPrint = $"{totalAmountLookedAt} bytes";
                            cutStringReverse = false;
                            break;
                        case 4:
                            prefix = "Progress:  ";
                            toPrint = $"{state.filesConfirmed.Count} same, {state.filesToFix.Count} need fix, {state.filesToCopy.Count} to copy, {state.systemCount} system files";
                            cutStringReverse = false;
                            break;
                        case 5:
                            prefix = "File:      ";
                            toPrint = currentFile;
                            break;
                        case 6:
                            prefix = "To:        ";
                            toPrint = destinationFolder;
                            break;
                        case 7:
                            prefix = "From:      ";
                            if (system)
                            {
                                prefix = "From(sys): ";
                                Console.ForegroundColor = ConsoleColor.Yellow;
                            }
                            toPrint = currentFolder;
                            break;
                    }
                    if((prefix.Length + toPrint.Length) >= Console.BufferWidth)
                    {
                        int lengthLimit = Console.BufferWidth - prefix.Length - 5;
                        if (lengthLimit <= 0 || lengthLimit > toPrint.Length) continue;
                        if (cutStringReverse)
                        {
                            toPrint = "... " + toPrint.Substring(toPrint.Length - lengthLimit, lengthLimit);
                        }
                        else
                        {
                            toPrint = "... " + toPrint.Substring(0, lengthLimit);
                        }
                    }

                    Console.SetCursorPosition(0, line);
                    Console.Write(prefix+toPrint);
                    Console.ForegroundColor = ConsoleColor.White;

                }
            } catch(Exception ex)
            {
                // no big deal its just the printing
            }
            Console.ForegroundColor = ConsoleColor.White;
        }

        static void CopyWithDate(string from, string to, bool withDateCreated)
        {
            FileInfo fi = new FileInfo(from);
            from = MakePathSafe(from);
            to = MakePathSafe(to);
            File.Copy(from, to);
            if (withDateCreated)
            {
                File.SetCreationTime(to, fi.CreationTime);
            }
            File.SetLastWriteTime(to,fi.LastWriteTime); // this is done automatically by file.copy i think but can't hurt to do it for safety in case it doesnt work on other OSes?
        }

        static void MakeFolderWithDate(string from, string to, bool withDateCreated)
        {
            DirectoryInfo di = new DirectoryInfo(from);
            from = NormalizePathEnding(MakePathSafe(from));
            to = NormalizePathEnding(MakePathSafe(to));
            Directory.CreateDirectory(to);
            if (withDateCreated)
            {
                Directory.SetCreationTime(to, di.CreationTime);
            }
            Directory.SetLastWriteTime(to, di.LastWriteTime); // this is done automatically by file.copy i think but can't hurt to do it for safety in case it doesnt work on other OSes?
        }


        // TODO more try here?
        static void DoFolderRecursive(string basePathReference, string basePathDestination, string reference, PostAnalysisState state, Stopwatch sw, bool system,string[] excludePaths)
        {
            try
            {

                if (!SafeDirExists(reference))
                {
                    Console.WriteLine($"DoFolderRecursive: Reference path {reference} doesn't exist. Exiting.");
                    return;
                }

                DirectoryInfo dirInfo = new DirectoryInfo(reference);

                if (dirInfo.Attributes.HasFlag(FileAttributes.System) && dirInfo.Parent != null) // when dirinfo.parent is null, it's a drive. a drive is a system folder but we don't wanna classify it as system
                {
                    system = true;
                }

                // check if this folder is excluded.
                string normalized = NormalizePathEnding(reference);
                foreach (string excluded in excludePaths)
                {
                    if (normalized.Equals(excluded,StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"DoFolderRecursive: Reference path {reference} skipped due to being an excluded path.");
                        return;
                    }
                }

                CancellationTokenSource cts = new CancellationTokenSource();

                string destinationFolder = SafePathCombine(basePathDestination, SafeGetRelPath(basePathReference,reference));
                if (cancelAnalysis)
                {
                    isAnalyzing = false;
                    Console.WriteLine("Aborting analysis. Continuing from current state of analysis.");
                    return;
                }
                string[] files = SafeDirGetFiles(reference);
                foreach(string file in files)
                {
                    if (cancelAnalysis)
                    {
                        isAnalyzing = false;
                        Console.WriteLine("Aborting analysis. Continuing from current state of analysis.");
                        return;
                    }
                    try
                    {

                        string fileRelative = SafeGetRelPath(basePathReference, file);
                        string fileRelativeThisFolder = SafeGetRelPath(reference, file);
                        string targetFile = SafePathCombine(basePathDestination, fileRelative);
                        if (!SafeFileExists(targetFile))
                        {
                            FileInfo info = new FileInfo(file);

                            //Console.WriteLine($"target file {targetFile} doesn't exist. must copy.");
                            state.filesToCopy.Add(new FileToCopy() { 
                                from=file,
                                to=targetFile,
                                existsAndDifferent = false,
                                size = (UInt64)info.Length,
                                sizeTarget = 0,
                                system = system
                            });
                            if (system)
                            {
                                state.systemCount++;
                            }
                            state.totalLookedAt += (UInt64)info.Length;
                            PrintUpdate(sw, reference, destinationFolder, fileRelativeThisFolder, state,system);
                            continue;
                        }
                        else
                        {
                            bool same;
                            UInt64 length1 = 0;
                            UInt64 length2 = 0;
                            using (FileStream fs1 = File.OpenRead(file))
                            {
                                length1 = (UInt64)fs1.Length;
                                using (FileStream fs2 = File.OpenRead(targetFile))
                                {
                                    length2 = (UInt64)fs2.Length;
                                    same = VectorizedComparer.Same(fs1, fs2, cts.Token, default, true);
                                }
                            }
                            state.totalLookedAt += length1;
                            state.totalCompared += length1;
                            PrintUpdate(sw, reference, destinationFolder, fileRelativeThisFolder, state, system);

                            if (!same)
                            {
                                state.filesToFix.Add(new FileToCopy()
                                {
                                    from = file,
                                    to = targetFile,
                                    existsAndDifferent = true,
                                    size = length1,
                                    sizeTarget = length2,
                                    system = system
                                });
                                if (system)
                                {
                                    state.systemCount++;
                                }
                            }
                            else
                            {
                                state.filesConfirmed.Add(new FileToCopy()
                                {
                                    from = file,
                                    to = targetFile,
                                    existsAndIdentical = true,
                                    size = length1,
                                    sizeTarget = length2,
                                    system = system
                                });
                                if (system)
                                {
                                    state.systemCount++;
                                }
                            }

                                //Console.WriteLine($"Comparison of {file} took {time} seconds. Files are same: {same}");
                            //if (!same) { 
                            //   Console.WriteLine($"target file {targetFile} is different. must copy.");
                            //}
                        }
                    }
                    catch (Exception ex2)
                    {

                        state.errors.Add($"Error analyzing file {file}: {ex2.ToString()}");
                    }
                }

                string[] folders = SafeDirGetDirs(reference);
                foreach (string folder in folders)
                {
                    if (cancelAnalysis)
                    {
                        isAnalyzing = false;
                        Console.WriteLine("Aborting analysis. Continuing from current state of analysis.");
                        return;
                    }
                    DoFolderRecursive(basePathReference, basePathDestination, folder, state,sw,system,excludePaths);
                }

            }
            catch (Exception ex)
            {
                state.errors.Add($"Error analyzing folder {reference}: {ex.ToString()}");
            }

        }

        // DO NOT call this with relative paths
        static string MakePathSafe(string path)
        {
            // we assume that anything going into this function is already a full (not relative) path.
            char startLetter = path[0];
            if (startLetter >='A' && startLetter <= 'Z' && path.Substring(1,2)==@":\") // prefix windows drive paths
            {
                return @$"\\?\{path}";
            }
            else
            {
                return path;
            }
        }
        static string MakePathUnsafe(string path)
        {
            if (path.StartsWith(@"\\?\"))
            {
                return path.Substring(@"\\?\".Length);
            }
            else
            {
                return path;
            }
        }
        static bool SafeDirExists(string folder)
        {
            bool exists = Directory.Exists(NormalizePathEnding(MakePathSafe(folder)));
            return exists;
        }
        static string SafeGetDirName(string path)
        {
            return Path.GetDirectoryName(MakePathSafe(path));
        }
        //static string SafeGetFullPath(string path)
        //{
        //    return Path.GetFullPath(MakePathSafe(path));
        //}
        static string SemiSafeGetFullPath(string path) // this isnt TRULY safe but we have to normalize paths going into the program once at least so stuff is somewhat consistent. we could be getting relative paths, paths with ../ etc
        {
            if(!path.EndsWith('/') && !path.EndsWith('\\'))
            {
                path += Path.DirectorySeparatorChar;
            }
            path = MakePathSafe(Path.GetFullPath(path));
            if (!path.EndsWith('/') && !path.EndsWith('\\'))
            {
                path += Path.DirectorySeparatorChar;
            }
            return path;
        }
        static string NormalizePathEnding(string path)
        {
            if (!path.EndsWith('/') && !path.EndsWith('\\'))
            {
                path += Path.DirectorySeparatorChar;
            }
            return path;
        }
        static string[] SafeDirGetFiles(string folder)
        {
            return Directory.GetFiles(NormalizePathEnding(MakePathSafe(folder)));
        }
        static string[] SafeDirGetDirs(string folder)
        {
            return Directory.GetDirectories(NormalizePathEnding(MakePathSafe(folder)));
        }
        static bool SafeFileExists(string file)
        {
            return File.Exists(MakePathSafe(file));
        }
        static string SafePathCombine(string a, string b)
        {
            bool aEndsWithSep = a.EndsWith('/') || a.EndsWith('\\');
            bool bStartsWithSep = b.StartsWith('/') || b.StartsWith('\\');
            if (aEndsWithSep && bStartsWithSep)
            {
                return a+b.Substring(1);
            }
            else if (!aEndsWithSep && !bStartsWithSep)
            {
                return a+Path.DirectorySeparatorChar+b;
            }
            else
            {
                return a+b;
            }
        }
        static string SafeGetRelPath(string a, string b)
        {
            // Path.GetRelativePath breaks with the safe prefix. idk
            //return Path.GetRelativePath(MakePathUnsafe(a), MakePathUnsafe(b));
            if (b.StartsWith(a,StringComparison.InvariantCultureIgnoreCase))
            {
                return b.Substring(a.Length);
            }
            else
            {
                return null;
            }
        }
        static void SafeFileDelete(string file)
        {
            File.Delete(MakePathSafe(file));
        }
        static void SafeFileCopy(string file, string file2)
        {
            File.Copy(MakePathSafe(file), MakePathSafe(file2));
        }
        static void SafeFileMove(string file, string file2)
        {
            File.Move(MakePathSafe(file), MakePathSafe(file2));
        }
        static void SafeCreateDirectory(string folder)
        {
            Directory.CreateDirectory(MakePathSafe(folder));
        }

    }
}
