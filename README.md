# LZXCompactLight

Compress files to NTFS LZX compression with minimal disk write cycles.

<b>Syntax: </b><br/>
LZXCompactLight [/log:mode] [/resetDb] [/scheduleOn] [/scheduleOff] [/? | /help] [filePath]

<b>Options:</b><br/>

<b>/log: None, General, Stat, FileCompacing, FileSkipping, Debug</b> - log flags (comma separated list).
General and Stat levels are outputed to both log file and the console, other levels to log file only.
Default value: /log:General, Stat

<b>/resetDb</b> - resets db. On next run, all files will be traversed by Compact command.

<b>/scheduleOn</b> - enables Task Scheduler entry to run LZXCompactLight when computer is idle for 10 minutes. Task runs daily.

<b>/scheduleOff</b> - disables Task Scheduler entry

<b>/?</b> or <b>/help</b> - displays this help screen

<b>filePath</b> - root path to start. All subdirectories will be traversed. Default is root of current drive, like c:\.

<b>Description:</b><br/>
Windows 10 extended NTFS compression with LZX alghorithm.
Files compressed with LZX can be opened like any other file because the uncompressing operation is transparent.
Compressing files with LZX is CPU intensive and thus is not being done automatically. It can be done only by running Compact command line.
To keep the files compressed, windows Compact command needs to be re-run. This can be done with Task Scheduler.

There is a catch with SSD drives though.
When Compact command is being run on file already LZX-compressed, it will not try to recompress it.
However, if file is not compressible, Compact will try to recompress it every time, writing temp data to disk.

This is an issue on SSD drives, because of limited write cycles.
LZXCompactLight keeps record of file name and its last seen size. If the file has not changed since last LZXCompactLight run, attempt for recomressing will not be made.
This saves SSD write cycles and speeds up processing time.
Iterating through files is multithreaded, one file per CPU logical core.

<b>Typical use:<br/></b>
LZXCompactLight /scheduleOn c:\
