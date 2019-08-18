# LZXCompactLight

Automatically compress files to NTFS LZX compression with minimal disk write cycles.
                
<b/>Syntax:</b> LZXCompactLight [/log:mode] [/resetDb] [/scheduleOn] [/scheduleOff] [/? | /help] [filePath]

<b>Options:</b>

<b>/log:</b> [None, General, Info, Debug] - log level. Default value: Info
<b>None</b>    - nothing is outputted
<b>General</b> - Session start / end timestamp, skipped folders
<b>Info</b>    - General + statistics about current session
<b>Debug</b>   - Info + information about every file

<b>/resetDb</b> - resets db. On next run, all files will be traversed by Compact command.

<b>/scheduleOn</b> - enables Task Scheduler entry to run LZXCompactLight when computer is idle for 10 minutes. Task runs daily.

<b>/scheduleOff</b> - disables Task Scheduler entry

<b>/?</b> or <b>/help</b> - displays this help screen

<b>filePath</b> - root path to start. All subdirectories will be traversed. Default is root of current drive, like c:\.

<b>Description:</b>
Windows 10 extended NTFS compression with LZX alghorithm. 
Files compressed with LZX can be opened like any other file because the uncompressing operation is transparent.
Compressing files with LZX is CPU intensive and thus is not being done automatically. When file is updated, it will be saved in uncompressed state.
To keep the files compressed, windows Compact command needs to be re-run. This can be done with Task Scheduler.

There is a catch with SSD drives though.
When Compact command is being run on file already LZX-compressed, it will not try to recompress it.
However, if file is not compressible (like .jpg image), Compact will try to recompress it every time, writing temp data to disk.

This is an issue on SSD drives, because of limited write cycles.
LZXCompactLight keeps record of file name and its last seen size. If the file has not changed since last LZXCompactLight run, it will be skipped. 
This saves SSD write cycles and also speeds up processing time, as on second run only newly updated / inserted files are processed.

If folder is found with NTFS compression enabled, after processing it will be marked as non-compressed. 
This is because LZX-compression does not use NTFS Compressed attribute.

Iterating through files is multithreaded, one file per CPU logical core.
For larger file accessibility, this command should be run with Adminstrator priviledges.

<b>Typical use:</b>
LZXCompactLight /scheduleOn c:\ 
