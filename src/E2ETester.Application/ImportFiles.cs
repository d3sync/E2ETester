namespace E2ETester.Application;
public static class ImportFiles
{
    public static Dictionary<string,string> References(string directory)
    {
        var root=Path.GetFullPath(directory);var result=new Dictionary<string,string>();long size=0;
        // Explicit directory selection authorizes these files; ignore junctions/symlinks.
        var options=new EnumerationOptions { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint };
        foreach(var file in Directory.EnumerateFiles(root,"*",options).Where(f=>new[]{".json",".yaml",".yml"}.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            size+=new FileInfo(file).Length;
            if(size>5_000_000 || result.Count>=32)throw new InvalidDataException("Reference directory exceeds 32 documents or 5 MB. Select a smaller directory.");
            result.Add(Path.GetRelativePath(root,file).Replace('\\','/'),File.ReadAllText(file));
        }
        return result;
    }
}
