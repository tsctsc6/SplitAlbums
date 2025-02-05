using System.CommandLine;
using System.Diagnostics;
using System.Text;
using CueSharp;

namespace SplitAlbums;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var fileOption = new Option<FileInfo>(
            name: "--file",
            description: "The .cue file.")
        {
            IsRequired = true,
        };
        fileOption.AddAlias("-f");
        
        var outDirOption = new Option<DirectoryInfo>(
            name: "--out-dir",
            description: "The output directory.")
        {
            IsRequired = true,
        };
        outDirOption.AddAlias("-o");
        
        var rootCommand = new RootCommand("According to cue file and album file, split into multiple songs.");
        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(outDirOption);
        
        rootCommand.SetHandler(StartSplit, fileOption, outDirOption);

        return await rootCommand.InvokeAsync(args);
    }
    
    static void StartSplit(FileInfo file, DirectoryInfo outDir)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb2312 = Encoding.GetEncoding("GB2312");
        var cue = new CueSheet(file.FullName, gb2312);
        var audioFileName = string.Empty;
        var artistName = string.Empty;
        
        for (var i = 0; i < cue.Tracks.Length - 1; i++)
        {
            artistName = string.IsNullOrEmpty(cue.Tracks[i].Performer) ? cue.Performer : cue.Tracks[i].Performer;
            if (!string.IsNullOrEmpty(cue.Tracks[i].DataFile.Filename)) audioFileName = cue.Tracks[i].DataFile.Filename;
            if (!string.IsNullOrEmpty(cue.Tracks[i + 1].DataFile.Filename))
            {
                // 这个 FILE 的最后一个 TRACK
                SplitLast($"{file.DirectoryName}\\{audioFileName}",
                    $"{cue.Tracks[i].Indices[^1].Minutes}:{cue.Tracks[i].Indices[^1].Seconds}",
                    outDir.FullName, $"{i + 1:00} {cue.Tracks[i].Title}.flac",
                    cue.Tracks[i].Title, artistName, cue.Title, i + 1, $"{file.DirectoryName}\\cover.jpg");
                continue;
            }
            var startIndex = cue.Tracks[i].Indices[^1];
            var endIndex = cue.Tracks[i + 1].Indices[0];
            
            Split($"{file.DirectoryName}\\{audioFileName}",
                $"{startIndex.Minutes}:{startIndex.Seconds}", $"{endIndex.Minutes}:{endIndex.Seconds + 1}",
                outDir.FullName, $"{i + 1:00} {cue.Tracks[i].Title}.flac",
                cue.Tracks[i].Title, artistName, cue.Title, i + 1, $"{file.DirectoryName}\\cover.jpg");
        }
        var lastTrack = cue.Tracks[^1];
        artistName = string.IsNullOrEmpty(lastTrack.Performer) ? cue.Performer : lastTrack.Performer;
        if (!string.IsNullOrEmpty(lastTrack.DataFile.Filename)) audioFileName = lastTrack.DataFile.Filename;
        SplitLast($"{file.DirectoryName}\\{audioFileName}",
            $"{lastTrack.Indices[^1].Minutes}:{lastTrack.Indices[^1].Seconds}",
            outDir.FullName, $"{cue.Tracks.Length:00} {lastTrack.Title}.flac",
            lastTrack.Title, artistName, cue.Title, cue.Tracks.Length, $"{file.DirectoryName}\\cover.jpg");
    }

    static void Split(string audioFileName, string startTime, string endTime, string outDir, string outFileName, string songName, string artistName, string albumName, int trackIndex, string coverFileName)
    {
        if(File.Exists($"{outDir}\\{outFileName}")) File.Delete($"{outDir}\\{outFileName}");
        using Process ffmpeg = new Process()
        {
            StartInfo =
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{audioFileName}\" -i \"{coverFileName}\" -map 0 -map 1 -ss {startTime} -to {endTime} -metadata title=\"{songName}\" -metadata artist=\"{artistName}\" -metadata album=\"{albumName}\" -metadata track=\"{trackIndex}\" -disposition:v attached_pic \"{outDir}\\{outFileName}\"",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        
        Console.WriteLine($"{ffmpeg.StartInfo.FileName} {ffmpeg.StartInfo.Arguments}");
        
        ffmpeg.Start();
        var ffmpegOutput = ffmpeg.StandardOutput.ReadToEnd();
        if (ffmpeg.ExitCode != 0) throw new ApplicationException($"ffmpeg.ExitCode is {ffmpeg.ExitCode},\n{ffmpegOutput}");
    }

    static void SplitLast(string audioFileName, string startTime, string outDir, string outFileName, string songName, string artistName, string albumName, int trackIndex, string coverFileName)
    {
        using Process ffprobe = new Process()
        {
            StartInfo =
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioFileName}\"",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        ffprobe.Start();
        var ffprobeOutput = ffprobe.StandardOutput.ReadToEnd();
        if (ffprobe.ExitCode != 0)
            throw new Exception($"Failed execute command: \n{ffprobe.StartInfo.FileName} {ffprobe.StartInfo.Arguments}\nresult: {ffprobeOutput}");
        Split(audioFileName, startTime, ffprobeOutput.TrimEnd(), outDir, outFileName, songName, artistName, albumName, trackIndex, coverFileName);
    }
}