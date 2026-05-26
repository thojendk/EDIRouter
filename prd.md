\#EDIRouter


\##Backgound

EDIROuter is a console app that can sort EDIFACT files by moving them to a subdirectory for each receipient.

The app can be run as a command line tool and examines all files in a specific folder. The receipient and testflag is identified from the UNB segment and the file is moved into a subdirectory in this format: outputpath\\<receipient>\\<environment> where environment can be test or prod. If the subdirectory does not exist in advance it is created on the fly.

Each file is supposed to consist of only one EDI interchange (UNB/UNZ) and only the UNB segment needs to be parsed. There can be an optional UNA segment before an UNB segment.

A file should not be moved if it is still open i.e. if a FTP program is still accessing it and it should be as fail safe as possible.

The app should maintain a log with the last 30 days of files processed.


\#Tech stack

Platform: .NET 9





