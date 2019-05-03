using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace LZXCompactLightEngine
{
    public class LZXCompactLightEngine
    {
        private const string dbFileName = "FileDict.db";
        private const string logFileName = "Activity.log";

        private readonly string[] skipCompression = new string[] { ".zip", ".gif", ".7z", ".bmp", ".jpeg", ".jpg", ".mov", ".mp3", ".avi", ".cab", ".mpeg" };
        private readonly BinaryFormatter binaryFormatter = new BinaryFormatter();
        private readonly object lockObject = new object();
        private readonly int maxQueueLength = Environment.ProcessorCount * 16;
        private readonly CancellationTokenSource cancelToken = new CancellationTokenSource();

        private ConcurrentDictionary<int, int> fileDict = new ConcurrentDictionary<int, int>();
        private int threadQueueLength = 0;

        public int fileCountSkippedByAttributes = 0;
        public int fileCountSkippedByExtension = 0;
        public int fileCountSkipByNoChanges = 0;
        public int fileCountProcessed = 0;

        public LogLevel LogLevel { get; set; } = LogLevel.General;

        public int FileDictCount
        {
            get
            {
                return fileDict?.Count ?? 0;
            }
        }

        public void ResetDb()
        {
            File.Delete(dbFileName);
        }

        public void Cancel()
        {
            Log("Terminating...", 4, LogLevel.General);
            cancelToken.Cancel();
        }

        public void Process(string path = "c:\\")
        {
            Log($"Starting path {path}", 2, LogLevel.General);

            DirectoryInfo diTop = new DirectoryInfo(path);

            try
            {
                foreach (var fi in diTop.EnumerateFiles())
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        // wait until all threads complete
                        WaitUntilThreadPoolComplete();

                        break;
                    }

                    try
                    {
                        ThreadPool.QueueUserWorkItem(a =>
                        {
                            ProcessFile(fi);
                        });

                        Interlocked.Increment(ref threadQueueLength);

                        // Do not let queue length more items than MaxQueueLength
                        while (threadQueueLength > maxQueueLength)
                        {
                            Thread.Sleep(200);
                        }
                    }
                    catch (UnauthorizedAccessException UnAuthTop)
                    {
                        Log(fi, UnAuthTop);
                    }
                }

                foreach (var di in diTop.EnumerateDirectories("*"))
                {
                    try
                    {
                        foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            if (cancelToken.IsCancellationRequested)
                            {
                                // wait until all threads complete
                                WaitUntilThreadPoolComplete();
                                break;
                            }

                            try
                            {
                                ThreadPool.QueueUserWorkItem(a =>
                                {
                                    ProcessFile(fi);
                                });

                                Interlocked.Increment(ref threadQueueLength);

                                // Do not let queue length more items than MaxQueueLength
                                while (threadQueueLength > maxQueueLength)
                                {
                                    Thread.Sleep(200);
                                }
                            }
                            catch (UnauthorizedAccessException UnAuthFile)
                            {
                                Log(fi, UnAuthFile);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException UnAuthSubDir)
                    {
                        Log($"UnAuthSubDir: {UnAuthSubDir.Message}");
                    }
                }
            }
            catch (DirectoryNotFoundException DirNotFound)
            {
                Log(DirNotFound.Message);
            }
            catch (UnauthorizedAccessException UnAuthDir)
            {
                Log($"UnAuthDir: {UnAuthDir.Message}");
            }
            catch (PathTooLongException LongPath)
            {
                Log(LongPath.Message);
            }
            catch (Exception ex)
            {
                Log($"Other error: {ex.Message}");
            }

            // wait until all threads complete
            WaitUntilThreadPoolComplete();

            Log("Completed");
        }

        private void WaitUntilThreadPoolComplete()
        {
            while (threadQueueLength > 0)
            {
                Thread.Sleep(200);
            }
        }

        private void ProcessFile(FileInfo fi)
        {
            try
            {
                if (skipCompression.Any(c => c == fi.Extension))
                {
                    Interlocked.Increment(ref fileCountSkippedByExtension);
                    return;
                }

                if (fi.Attributes.HasFlag(FileAttributes.System) || fi.Attributes.HasFlag(FileAttributes.Compressed))
                {
                    Interlocked.Increment(ref fileCountSkippedByAttributes);
                    return;
                }

                if (fi.Length > 0)
                {
                    Log("", 4, LogLevel.FileCompacting | LogLevel.FileSkipping);

                    int filePathHash = fi.FullName.GetHashCode();

                    int fileSizeHash;
                    if (fileDict.TryGetValue(filePathHash, out fileSizeHash) && fileSizeHash == fi.Length.GetHashCode())
                    {
                        Log($"Skipping file: ${fi.FullName} because it has been visited already and its size did not change", 1, LogLevel.FileSkipping);
                        Interlocked.Increment(ref fileCountSkipByNoChanges);
                        return;
                    }

                    fileDict[filePathHash] = fi.Length.GetHashCode();

                    Log($"Compressing file {fi.FullName}", 1, LogLevel.FileCompacting);
                    Interlocked.Increment(ref fileCountProcessed);

                    var proc = new Process();
                    proc.StartInfo.FileName = $"compact";
                    proc.StartInfo.Arguments = $"/c /exe:LZX \"{fi.FullName}\"";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.Start();
                    string outPut = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    proc.Close();

                    Log(outPut, 2, LogLevel.Debug);
                }
            }
            finally
            {
                Interlocked.Decrement(ref threadQueueLength);
            }
        }


        public void Log(FileInfo fi, Exception ex)
        {
            Log($"Error during processing: file: {fi.FullName}, exception message: {ex.Message}");
        }

        public void Log(string str, int newLine = 1, LogLevel level = LogLevel.General)
        {
            if (!LogLevel.HasFlag(level))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < newLine; i++, sb.AppendLine()) ;

            if (!string.IsNullOrEmpty(str))
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + str);

            string result = sb.ToString();

            if (LogLevel.HasFlag(LogLevel.General) && level.HasFlag(LogLevel.General) ||
                LogLevel.HasFlag(LogLevel.Stat) && level.HasFlag(LogLevel.Stat))
            {
                Console.WriteLine(result);
            }

            lock (lockObject)
            {
                File.AppendAllText(logFileName, result);
            }
        }

        public void SaveToFile()
        {
            try
            {
                Log("Saving file...");
                lock (lockObject)
                {
                    using (FileStream writerFileStream = new FileStream(dbFileName, FileMode.Create, FileAccess.Write))
                    {
                        binaryFormatter.Serialize(writerFileStream, fileDict);
                        writerFileStream.Close();
                    }
                }

                Log("File saved");
            }
            catch (Exception ex)
            {
                Log($"Unable to save dic to file, {ex.Message}");
            }
        }

        public void LoadFromFile()
        {
            if (File.Exists(dbFileName))
            {
                try
                {
                    Log("DB file found");

                    using (FileStream readerFileStream = new FileStream(dbFileName, FileMode.Open, FileAccess.Read))
                    {
                        if (readerFileStream.Length > 0)
                        {
                            fileDict = (ConcurrentDictionary<int, int>)this.binaryFormatter.Deserialize(readerFileStream);
                            readerFileStream.Close();
                        }
                    }

                    Log("Loaded from file");
                }
                catch (Exception ex)
                {
                    Log($"Error during loading from file: {ex.Message}" +
                        $"{Environment.NewLine}Terminating.");

                    Environment.Exit(0);
                }
            }
            else
            {
                Log("DB file not found");
            }
        }
    }

    [Flags]
    public enum LogLevel
    {
        None = 0,
        General = 1,
        Stat = 2,
        FileCompacting = 4,
        FileSkipping = 8,
        Debug = 16
    }
}
