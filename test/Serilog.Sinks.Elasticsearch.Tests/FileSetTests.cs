using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using Serilog.Sinks.Elasticsearch.Durable;
using Xunit;

namespace Serilog.Sinks.Elasticsearch.Tests;

public class FileSetTests : IDisposable
{
    private readonly string _fileNameBase;
    private readonly string _tempFileFullPathTemplate;
    private Dictionary<RollingInterval, string> _bufferFileNames;

    public FileSetTests()
    {
        _fileNameBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _tempFileFullPathTemplate = _fileNameBase + "-{0}.json";
    }

    public void Dispose()
    {
        foreach (var bufferFileName in _bufferFileNames.Values) System.IO.File.Delete(bufferFileName);
    }

    [Theory]
    [InlineData(RollingInterval.Day)]
    [InlineData(RollingInterval.Hour)]
    [InlineData(RollingInterval.Infinite)]
    [InlineData(RollingInterval.Minute)]
    [InlineData(RollingInterval.Month)]
    [InlineData(RollingInterval.Year)]
    // Ensures that from all presented files FileSet gets only files with specified rolling interval and not the others.  
    public void GetBufferFiles_ReturnsOnlySpecifiedTypeOfRollingFile(RollingInterval rollingInterval)
    {
        // Arrange
        var format = rollingInterval.GetFormat();
        _bufferFileNames = GenerateFilesUsingFormat(format);
        var fileSet = new FileSet(_fileNameBase, rollingInterval);
        var bufferFileForInterval = _bufferFileNames[rollingInterval];

        // Act
        var bufferFiles = fileSet.GetBufferFiles();

        // Assert
        bufferFiles.Should().BeEquivalentTo(bufferFileForInterval);
    }

    [Fact]
    // Ensures that date format "yyyyMMdd-HH" is supported by Hourly interval. This date format was for Hourly files before move to standard "yyyyMMddHH"
    public void GetBufferFiles_SupportOldHourlyFormat()
    {
        // Arrange
        var rollingInterval = RollingInterval.Hour;
        var format = "yyyyMMdd-HH";
        _bufferFileNames = GenerateFilesUsingFormat(format);
        var fileSet = new FileSet(_fileNameBase, rollingInterval);
        var bufferFileForInterval = _bufferFileNames[rollingInterval];

        // Act
        var bufferFiles = fileSet.GetBufferFiles();

        // Assert
        bufferFiles.Should().BeEquivalentTo(bufferFileForInterval);
    }


    [Fact]
    // Ensures that both date formats are supported simultaneously: "yyyyMMdd-HH"(old) and "yyyyMMddHH" (new)
    public void GetBufferFiles_BothNewAndOldHourlyFormatsAreSupported_andOldSortedFirst()
    {
        // Arrange
        var rollingInterval = RollingInterval.Hour;
        var oldFormat = "yyyyMMdd-HH";
        var newFormat = rollingInterval.GetFormat();
        _bufferFileNames = new Dictionary<RollingInterval, string>
        {
            {RollingInterval.Hour, GenerateBufferFile(oldFormat, oldFormat)},
            // adding with arbitrary interval to Dictionary for a proper clean up (already have Hour filled)
            {RollingInterval.Day, GenerateBufferFile(oldFormat, newFormat)}
        };
        var fileSet = new FileSet(_fileNameBase, rollingInterval);
        var hourlyBufferFileForOldFormat = _bufferFileNames[RollingInterval.Hour];
        var hourlyBufferFileForNewFormat = _bufferFileNames[RollingInterval.Day];

        // Act
        var bufferFiles = fileSet.GetBufferFiles();

        // Assert
        bufferFiles.ShouldBeEquivalentTo(new[] {hourlyBufferFileForOldFormat, hourlyBufferFileForNewFormat},
            options => options.WithStrictOrdering());
    }

    /// <summary>
    ///     Generates buffer files for all RollingIntervals and returns dictionary of {rollingInterval, fileName} pairs.
    /// </summary>
    /// <param name="format"></param>
    /// <returns></returns>
    private Dictionary<RollingInterval, string> GenerateFilesUsingFormat(string format)
    {
        var result = new Dictionary<RollingInterval, string>();
        foreach (var rollingInterval in Enum.GetValues(typeof(RollingInterval)))
        {
            var bufferFileName = GenerateBufferFile(format, rollingInterval.ToString());
            result.Add((RollingInterval) rollingInterval, bufferFileName);
        }

        return result;
    }

    private string GenerateBufferFile(string format, string content)
    {
        var bufferFileName = string.Format(_tempFileFullPathTemplate,
            string.IsNullOrEmpty(format) ? string.Empty : new DateTime(2000, 1, 1).ToString(format));
        var lines = new[] {content};
        // Important to use UTF8 with BOM if we are starting from 0 position 
        System.IO.File.WriteAllLines(bufferFileName, lines, new UTF8Encoding(true));
        return bufferFileName;
    }
}