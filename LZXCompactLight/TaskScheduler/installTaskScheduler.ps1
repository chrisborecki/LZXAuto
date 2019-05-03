Register-ScheduledTask -xml (Get-Content 'Compacting.xml' | Out-String) -TaskName "CompactExtension" -User SYSTEM –Force

'Completed. Press Enter to exit.'
pause