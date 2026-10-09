using System;
using FileLib;

class Program {
    static void Main() {
        Console.WriteLine(DirectoryHelper.GetAppDataDirPath());
        Console.WriteLine(DirectoryHelper.GetCacheDirPath());
    }
}
