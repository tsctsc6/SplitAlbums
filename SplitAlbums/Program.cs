using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        var sampleRateOption = new Option<int?>(
            name: "-ar",
            description: "The sample rate.");
        
        var sampleFormatOption = new Option<string?>(
            name: "-sample_fmt",
            description: "The sample format.");

        var noCoverOption = new Option<bool>(
            name: "--no-cover",
            description: "Do not show cover image.")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        
        var rootCommand = new RootCommand("According to cue file and album file, split into multiple songs.");
        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(outDirOption);
        rootCommand.AddOption(sampleRateOption);
        rootCommand.AddOption(sampleFormatOption);
        rootCommand.AddOption(noCoverOption);
        
        rootCommand.SetHandler(StartSplit, fileOption, outDirOption, sampleRateOption, sampleFormatOption, noCoverOption);

        return await rootCommand.InvokeAsync(args);
    }
    
    static void StartSplit(FileInfo file, DirectoryInfo outDir, int? sampleRate, string? sampleFormat, bool noCover)
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
                    $"{GetCutPoint(cue.Tracks[i].Indices):0.000000}",
                    outDir.FullName, $"{i + 1:00} {cue.Tracks[i].Title}.flac",
                    cue.Tracks[i].Title, artistName, cue.Title, i + 1, $"{file.DirectoryName}\\cover.jpg", sampleRate, sampleFormat, noCover);
                continue;
            }
            
            var startCutPoint = GetCutPoint(cue.Tracks[i].Indices);
            var endCutPoint = GetCutPoint(cue.Tracks[i + 1].Indices);
            
            Split($"{file.DirectoryName}\\{audioFileName}",
                $"{startCutPoint:0.000000}", $"{endCutPoint:0.000000}",
                outDir.FullName, $"{i + 1:00} {cue.Tracks[i].Title}.flac",
                cue.Tracks[i].Title, artistName, cue.Title, i + 1, $"{file.DirectoryName}\\cover.jpg", sampleRate, sampleFormat, noCover);
        }
        var lastTrack = cue.Tracks[^1];
        artistName = string.IsNullOrEmpty(lastTrack.Performer) ? cue.Performer : lastTrack.Performer;
        if (!string.IsNullOrEmpty(lastTrack.DataFile.Filename)) audioFileName = lastTrack.DataFile.Filename;
        SplitLast($"{file.DirectoryName}\\{audioFileName}",
            $"{GetCutPoint(cue.Tracks[^1].Indices):0.000000}",
            outDir.FullName, $"{cue.Tracks.Length:00} {lastTrack.Title}.flac",
            lastTrack.Title, artistName, cue.Title, cue.Tracks.Length, $"{file.DirectoryName}\\cover.jpg", sampleRate, sampleFormat, noCover);
    }

    static double GetCutPoint(CueSharp.Index[] indices)
    {
        return indices.Length switch
        {
            1 => indices[0].Minutes * 60 + indices[0].Seconds + indices[0].Frames / 75.0,
            2 => (indices[0].Minutes * 60 + indices[0].Seconds + indices[0].Frames / 75.0 +
                  indices[1].Minutes * 60 + indices[1].Seconds + indices[1].Frames / 75.0) / 2.0,
            _ => -1.0
        };
    }
    
    static void Split(string audioFileName, string startTime, string endTime, string outDir, string outFileName, string songName, string artistName, string albumName, int trackIndex, string coverFileName, int? sampleRate, string? sampleFormat, bool noCover)
    {
        if (File.Exists($"{outDir}\\{trackIndex}.flac")) File.Delete($"{outDir}\\{trackIndex}.flac");
        if(File.Exists($"{outDir}\\{outFileName}")) File.Delete($"{outDir}\\{outFileName}");
        var argsCut = string.Empty;
        var sample_args = string.Empty;
        if (sampleRate != null)
        {
            sample_args += $"-ar {sampleRate} ";
        }
        if (sampleFormat != null)
        {
            sample_args += $"-sample_fmt {sampleFormat} ";
        }
        if (noCover)
        {
            argsCut =
                $"-v error -i \"{audioFileName}\" -ss {startTime} -to {endTime} -metadata title=\"{songName}\" -metadata artist=\"{artistName}\" -metadata album=\"{albumName}\" -metadata track=\"{trackIndex}\" -c:a flac {sample_args}\"{outDir}\\{outFileName}\"";
        }
        else
        {
            argsCut =
                $"-v error -i \"{audioFileName}\" -ss {startTime} -to {endTime} -metadata title=\"{songName}\" -metadata artist=\"{artistName}\" -metadata album=\"{albumName}\" -metadata track=\"{trackIndex}\" -c:a flac {sample_args}\"{outDir}\\{trackIndex}.flac\"";
        }
        var argsPic =
            $"-v error -i \"{outDir}\\{trackIndex}.flac\" -i \"{coverFileName}\" -c:v mjpeg -map 0 -map 1 -disposition:v:0 attached_pic -c:a copy \"{outDir}\\{outFileName}\"";
        
        using Process ffmpeg = new Process()
        {
            StartInfo =
            {
                FileName = "ffmpeg",
                Arguments = argsCut,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        
        Console.WriteLine($"{ffmpeg.StartInfo.FileName} {ffmpeg.StartInfo.Arguments}");
        
        ffmpeg.Start();
        var ffmpegError = ffmpeg.StandardError.ReadToEnd();
        if (ffmpeg.ExitCode != 0)
        {
            ShowErrorAndExit($"{Environment.NewLine}ffmpeg.ExitCode is {ffmpeg.ExitCode},{Environment.NewLine}{ffmpegError}", 1);
        }
        
        if (noCover) return;
        
        using Process ffmpeg2 = new Process()
        {
            StartInfo =
            {
                FileName = "ffmpeg",
                Arguments = argsPic,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        
        Console.WriteLine($"{ffmpeg2.StartInfo.FileName} {ffmpeg2.StartInfo.Arguments}");
        
        ffmpeg2.Start();
        ffmpegError = ffmpeg2.StandardError.ReadToEnd();
        if (ffmpeg2.ExitCode != 0)
        {
            ShowErrorAndExit($"{Environment.NewLine}ffmpeg.ExitCode is {ffmpeg.ExitCode},{Environment.NewLine}{ffmpegError}", 1);
        }
        
        if (File.Exists($"{outDir}\\{trackIndex}.flac")) File.Delete($"{outDir}\\{trackIndex}.flac");
    }

    static void SplitLast(string audioFileName, string startTime, string outDir, string outFileName, string songName, string artistName, string albumName, int trackIndex, string coverFileName, int? sampleRate, string? sampleFormat, bool noCover)
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
        {
            ShowErrorAndExit(
                $"Failed execute command: {Environment.NewLine}{ffprobe.StartInfo.FileName} {ffprobe.StartInfo.Arguments}{Environment.NewLine}result: {ffprobeOutput}",
                1);
        }
        Split(audioFileName, startTime, ffprobeOutput.TrimEnd(), outDir, outFileName, songName, artistName, albumName, trackIndex, coverFileName, sampleRate, sampleFormat, noCover);
    }
    
    [DoesNotReturn]
    static void ShowErrorAndExit(string error, int exitCode)
    {
        var defaultColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(error);
        Console.ForegroundColor = defaultColor;
        Environment.Exit(exitCode);
    }
}