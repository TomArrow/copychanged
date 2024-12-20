using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace copychanged
{

    class FileToCopy {
        public string from { get; set; }
        public string to { get; set; }
        public UInt64 size { get; set; }
        public UInt64 sizeTarget { get; set; }
        public bool existsAndDifferent { get; set; }
    }

    class PostAnalysisState
    {
        public string folder1 { get; set; }  = null;
        public string folder2 { get; set; }  = null;
        public List<FileToCopy> filesToCopy { get; set; }  = new List<FileToCopy>();
        public List<FileToCopy> filesToFix { get; set; }  = new List<FileToCopy>();
        public UInt64 totalCompared { get; set; }  = 0;
        public double totalSecondsTaken { get; set; }  = 0;

    }


    class Program
    {
        static void PrintHelp()
        {

            Console.WriteLine("Usage: copychanged [-n,--donew] [-c,--dochanged] referencefolder destinationfolder");
            Console.WriteLine("Optional param meaning:");
            Console.WriteLine("-n,--donew        Copy files that didn't exist yet.");
            Console.WriteLine("-c,--dochanged    Copy files that are different (overwrite!).");
            Console.WriteLine("-l,--log          Log what was done into _copychanged_copy.log and _copychanged_fix.log.");
            Console.WriteLine("-v,--verbose      Logging will include list of files that need to be copied (not shown by default to avoid spam). By default, _copychanged_copy.log only contains any errors or retries during copying and the count of files.");
            Console.WriteLine("-s,--save         Saves a serialized list of files that need to be processed in _copychanged_list.json");
            Console.WriteLine("-l,--load         Loads a serialized list of files that need to be processed from _copychanged_list.json");
            Console.WriteLine("By default you are only told the amount of needed copy operations, no copying happens.");
            Console.WriteLine("Copy operations are ALWAYS verified and will be repeated if a difference is found.");
            Console.WriteLine("--save and --load work INDEPENDENTLY of --donew and --dochanged. You can do both or either.");
        }

        static JsonSerializerOptions jsonOpts = new JsonSerializerOptions() {NumberHandling= System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals| System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString, WriteIndented = true };

        static PostAnalysisState RunCompare(string folder1, string folder2)
        {
            PostAnalysisState state = new PostAnalysisState();
            state.folder1 = Path.GetFullPath(folder1);
            state.folder2 = Path.GetFullPath(folder2);

            state.filesToCopy = new List<FileToCopy>();
            state.filesToFix = new List<FileToCopy>();

            state.totalCompared = 0;

            Console.Write("\nStarting file compare...");

            Stopwatch sw = new Stopwatch();

            sw.Start();

            DoFolderRecursive(state.folder1, state.folder2, state.folder1, state, sw);

            sw.Stop();

            state.totalSecondsTaken = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;

            return state;
        }

        static void Main(string[] args)
        {
            if(args.Length < 2)
            {
                PrintHelp();
                return;
            }

            bool doNew = false;
            bool doChanged = false;
            bool doLog = false;
            bool verboseLog = false;
            bool doSave = false;
            bool doLoad = false;

            string folder1 = null;
            string folder2 = null;

            foreach(string arg in args)
            {
                if (arg.Equals("--donew",StringComparison.OrdinalIgnoreCase) || arg.Equals("-n", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--donew/-n option detected. Will copy files that don't exist yet.");
                    doNew = true;
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

            PostAnalysisState state = null;

            if (!doLoad)
            {
                if(string.IsNullOrWhiteSpace(folder1) || string.IsNullOrWhiteSpace(folder2))
                {
                    Console.WriteLine($"Not loading list, and didn't specify two folders.");
                    PrintHelp();
                    return;
                }
                state = RunCompare(folder1, folder2);

            }
            else
            {
                if (!File.Exists("_copychanged_list.json"))
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

            if (doSave)
            {
                string json = JsonSerializer.Serialize(state, jsonOpts);
                if (File.Exists("_copychanged_list.json")) // make up to 2 backups just in case. shitty way of doing it tho xd.
                {
                    if (File.Exists("_copychanged_list.json.bak"))
                    {
                        if (File.Exists("_copychanged_list.json.finalbak"))
                        {
                            File.Delete("_copychanged_list.json.finalbak");
                        }
                        File.Move("_copychanged_list.json.bak", "_copychanged_list.json.finalbak");
                    }
                    File.Move("_copychanged_list.json", "_copychanged_list.json.bak");
                }
                File.WriteAllText("_copychanged_list.json",json);
            }



            Console.WriteLine();
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
                        sb.AppendLine($"{fileToFix.from} ({size1} bytes) --> {fileToFix.to} ({size2} bytes)");
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
                        if (verboseLog)
                        {
                            string size1 = fileToCopy.size.ToString("#,##0");
                            sb.AppendLine($"{fileToCopy.from} ({size1} bytes) --> {fileToCopy.to} (doesn't exist)");
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

            Int64 totalSuccess = 0;
            Int64 totalFail = 0;
            if (doChanged && state.filesToFix.Count > 0)
            {
                Console.WriteLine("Overwriting changed files now"); 
                foreach (FileToCopy fileToFix in state.filesToFix)
                {
                    Console.Write($"{fileToFix.to}...");
                    int currentAttempt = 0;
                    try
                    {
                        bool different = true;
                        while (different)
                        {
                            if (File.Exists(fileToFix.to))
                            {
                                File.Delete(fileToFix.to);
                            } else
                            {
                                Console.Write($"WEIRD, target file gone? Copying...");
                            }
                            File.Copy(fileToFix.from, fileToFix.to);
                            Console.Write($"Copied, verifying...");
                            different = !FilesAreSame(fileToFix.from, fileToFix.to,cts.Token);
                            if (different)
                            {
                                currentAttempt++;
                                Console.Write($"Overwritten file is STILL different. Retry {currentAttempt}....");
                            }
                            else
                            {
                                totalSuccess++;
                                if (doLog && (verboseLog || currentAttempt > 0))
                                {
                                    File.AppendAllText("_copychanged_fix.log", $"{fileToFix.to} successfully updated ({currentAttempt} retries)\n");
                                }
                                Console.Write($"File successfully updated.");
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
                    Console.Write($"{fileToCopy.to}...");
                    int currentAttempt = 0;
                    try
                    {
                        bool different = true;
                        while (different)
                        {
                            if (File.Exists(fileToCopy.to))
                            {
                                if (currentAttempt == 0)
                                {
                                    Console.Write($"wtf already exists... deleting...");
                                }
                                File.Delete(fileToCopy.to);
                            } else if (currentAttempt != 0)
                            {
                                Console.Write($"WEIRD, target file gone? Copying again...");
                            }
                            File.Copy(fileToCopy.from, fileToCopy.to);
                            Console.Write($"Copied, verifying...");
                            different = !FilesAreSame(fileToCopy.from, fileToCopy.to,cts.Token);
                            if (different)
                            {
                                currentAttempt++;
                                Console.Write($"Copied file is STILL different. Retry {currentAttempt}....");
                            }
                            else
                            {
                                totalSuccess++;
                                if (doLog && (verboseLog || currentAttempt > 0))
                                {
                                    File.AppendAllText("_copychanged_copy.log", $"{fileToCopy.to} successfully copied ({currentAttempt} retries)\n");
                                }
                                Console.Write($"File successfully copied.");
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

        // TODO more try here?
        static void DoFolderRecursive(string basePathReference, string basePathDestination, string reference, PostAnalysisState state, Stopwatch sw)
        {
            if (!Directory.Exists(reference))
            {
                Console.WriteLine($"DoFolderRecursive: Reference path {reference} doesn't exist. Exiting.");
                return;
            }
            CancellationTokenSource cts = new CancellationTokenSource();

            string destinationFolder = Path.Combine(basePathDestination,Path.GetRelativePath(basePathReference,reference));
            string[] files = Directory.GetFiles(reference);
            foreach(string file in files)
            {
                string targetFile = Path.Combine(basePathDestination, Path.GetRelativePath(basePathReference, file));
                if (!File.Exists(targetFile))
                {
                    FileInfo info = new FileInfo(file);

                    //Console.WriteLine($"target file {targetFile} doesn't exist. must copy.");
                    state.filesToCopy.Add(new FileToCopy() { 
                        from=file,
                        to=targetFile,
                        existsAndDifferent = false,
                        size = (UInt64)info.Length,
                        sizeTarget = 0
                    });
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
                    double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                    state.totalCompared += length1;

                    double bytesPerSecond = state.totalCompared / time;
                    string bpsString = bytesPerSecond.ToString("#,##0.00");
                    Console.Write($"\r{bpsString} bytes per second");

                    if (!same)
                    {
                        state.filesToFix.Add(new FileToCopy()
                        {
                            from = file,
                            to = targetFile,
                            existsAndDifferent = true,
                            size = length1,
                            sizeTarget = length2
                        });
                    }

                    //Console.WriteLine($"Comparison of {file} took {time} seconds. Files are same: {same}");
                    //if (!same) { 
                    //   Console.WriteLine($"target file {targetFile} is different. must copy.");
                    //}
                }
            }

            string[] folders = Directory.GetDirectories(reference);
            foreach (string folder in folders)
            {
                DoFolderRecursive(basePathReference, basePathDestination, folder, state,sw);
            }

        }

    }
}
