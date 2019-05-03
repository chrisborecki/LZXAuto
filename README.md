# LZXCompactLight
Compress files to NTFS LZX compression with minimal disk write cycles (keeps list of uncompressible files).
                
Syntax: LZXCompactLight [/log:mode] [/resetDb] [/scheduleOn] [/scheduleOff] [/? | /help] [filePath]

Options:

/log: None, General, Stat, FileCompacing, FileSkipping, Debug - log level (comma separated list).
General and Stat levels are outputed to the console, other levels to log file only.
Default value: /log:General,Stat

/resetDb - resets db. On next run, all files will be traversed by Compact command.

/scheduleOn - enables Task Scheduler entry to run LZXCompactLight when computer is idle for 10 minutes. Task runs daily.

/scheduleOff - disables Task Scheduler entry

/? or /help - displays this help screen

filePath - root path to start. All subdirectories will be traversed. Default is current drive.

Description:

Windows 10 extended NTFS compression with LZX alghorithm. 
Files compressed with LZX can be opened like any other file, the uncompressing operation is transparent.
Compressing files, however, can be done only by running Compact command line. 
To keep the files compressed, windows Compact command needs to be re-run. This could be easily achieved with Task Scheduler.

There is a catch with SSD drives though.
When Compact command is being run on file already LZX-compressed, it will not try to recompress it.
However, if file is not compressible, Compact will try to recompress it every time, writing temp data to disk.

This is an issue on SSD drives, because of limited write cycles.
LZXCompactLight keeps record of file name and its last seen size. If the file has not changed since last LZXCompactLight run, attempt for recompressing will not be made. 
This saves SSD write cycles and speeds up processing time.
Iterating through files is multithreaded, one file per CPU logical core.

Typical use:
LZXCompactLight /scheduleOn c:\ 
