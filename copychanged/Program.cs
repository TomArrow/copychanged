using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace copychanged
{

    class FileToCopy {
        public string from;
        public string to;
        public UInt64 size;
        public UInt64 sizeTarget;
        public bool existsAndDifferent;
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
            Console.WriteLine("By default you are only told the amount of needed copy operations, no copying happens.");
            Console.WriteLine("Copy operations are ALWAYS verified and will be repeated if a difference is found.");
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

            string folder1 = null;
            string folder2 = null;

            foreach(string arg in args)
            {
                if (arg.Equals("--donew",StringComparison.OrdinalIgnoreCase) || arg.Equals("-n", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--donew/-n option detected. Will copy files that don't exist yet.");
                    doNew = true;
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

            folder1 = Path.GetFullPath(folder1);
            folder2 = Path.GetFullPath(folder2);

            List<FileToCopy> filesToCopy = new List<FileToCopy>();
            List<FileToCopy> filesToFix = new List<FileToCopy>();

            UInt64 totalCompared = 0;

            Console.Write("\nStarting file compare...");
            
            Stopwatch sw = new Stopwatch();

            sw.Start();

            DoFolderRecursive(folder1, folder2, folder1, filesToCopy,filesToFix, ref totalCompared, sw);

            Console.WriteLine();
            Console.WriteLine($"{filesToCopy.Count} files don't exist yet.");
            Console.WriteLine($"{filesToFix.Count} files are different:");
            foreach(FileToCopy fileToFix in filesToFix)
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
                UInt64 totalCount = (UInt64)filesToFix.Count;
                UInt64 totalSize = 0;
                UInt64 totalSizeDest = 0;
                if (filesToFix.Count > 0)
                {
                    foreach (FileToCopy fileToFix in filesToFix)
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
                totalCount = (UInt64)filesToCopy.Count;
                totalSize = 0;
                if (filesToCopy.Count > 0)
                {
                    foreach (FileToCopy fileToCopy in filesToCopy)
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
            if (doChanged && filesToFix.Count > 0)
            {
                Console.WriteLine("Overwriting changed files now"); 
                foreach (FileToCopy fileToFix in filesToFix)
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
            if (doNew && filesToCopy.Count > 0)
            {
                Console.WriteLine("Copying new files now"); 
                foreach (FileToCopy fileToCopy in filesToCopy)
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
        static void DoFolderRecursive(string basePathReference, string basePathDestination, string reference, List<FileToCopy> filesToCopy, List<FileToCopy> filesToFix, ref UInt64 totalCompared, Stopwatch sw)
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
                    filesToCopy.Add(new FileToCopy() { 
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
                    totalCompared += length1;

                    double bytesPerSecond = totalCompared / time;
                    string bpsString = bytesPerSecond.ToString("#,##0.00");
                    Console.Write($"\r{bpsString} bytes per second");

                    if (!same)
                    {
                        filesToFix.Add(new FileToCopy()
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
                DoFolderRecursive(basePathReference, basePathDestination, folder, filesToCopy,filesToFix,ref totalCompared,sw);
            }

        }

    }
}
