using BankOcr.Domain;
using FunLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BankOcr.Cli
{
    public static class FileReader
    {
        public static Maybe<IEnumerable<string>> ToLines(this string filename)
        {
            return Maybe<IEnumerable<string>>.None;
        }

        public static IEnumerable<string> Lines(TextReader reader)
        {
            return LinesEnumerable(reader);

            static IEnumerable<string> LinesEnumerable(TextReader reader)
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }

        public static IEnumerable<Account> ToAccounts(this IEnumerable<string> lines)
        {
            using var enlines = lines.GetEnumerator();

            var maybeRows = GetFirstThreeRows(enlines);
            while (maybeRows)
            {
                yield return GetNineDigits(maybeRows.Value.Top, maybeRows.Value.Middle, maybeRows.Value.Bottom).FromDigits();
                maybeRows = GetNextThreeRows(enlines);
            }

            static Maybe<(string Top, string Middle, string Bottom)> GetFirstThreeRows(IEnumerator<string> enlines)
            {
                var maybeTop = enlines.Next();
                return maybeTop ?
                    (maybeTop.Value, enlines.Next().Value, enlines.Next().Value) :
                    Maybe<(string Top, string Middle, string Bottom)>.None;
            }

            static Maybe<(string Top, string Middle, string Bottom)> GetNextThreeRows(IEnumerator<string> enlines)
            {
                DiscardEmptyRow(enlines);
                return GetFirstThreeRows(enlines);
            }

            static void DiscardEmptyRow(IEnumerator<string> enlines) =>
                enlines.Next();

            static IEnumerable<Digit> GetNineDigits(string top, string middle, string bottom)
            {
                for (int i = 0; i < 9; i++)
                {
                    Func<string, string> ThreeCharsAtOffset = (s => ThreeCharsAt(s, OffsetFromIndex(i)));

                    var maybeStrings = new [] { ThreeCharsAtOffset(top), ThreeCharsAtOffset(middle), ThreeCharsAtOffset(bottom) };
                    var rowToDigitMaps = new Func<string, Digit> [] { TopRowToDigit, MiddleRowToDigit, BottomRowToDigit };

                    yield return maybeStrings
                        .Zip(rowToDigitMaps)
                        .Select(MapRowToDigit)
                        .Aggregate(Digit.All, (accumulator, digit) => accumulator * digit);
                }

                static int OffsetFromIndex(int index) => index * 3;
                static string ThreeCharsAt(string s, int offset) => s.Substring(offset, 3);
                static Digit MapRowToDigit((string row, Func<string, Digit> rowToDigit) pair) => pair.rowToDigit(pair.row);

                static Digit TopRowToDigit(string topRow) =>
                    topRow switch
                    {
                        " _ " => Digit.Zero + Digit.Two + Digit.Three + Digit.Five +
                                Digit.Six + Digit.Seven + Digit.Eight + Digit.Nine,
                        "   " => Digit.One + Digit.Four,
                        _ => throw new ArgumentException($"Invalid pattern [{topRow}]", nameof(topRow))
                    };

                static Digit MiddleRowToDigit(string middleRow) =>
                    middleRow switch
                    {
                        "| |" => Digit.Zero,
                        "  |" => Digit.One + Digit.Seven,
                        " _|" => Digit.Two + Digit.Three,
                        "|_|" => Digit.Four + Digit.Eight + Digit.Nine,
                        "|_ " => Digit.Five + Digit.Six,
                        _ => throw new ArgumentException($"Invalid pattern [{middleRow}]", nameof(middleRow))
                    };

                static Digit BottomRowToDigit(string bottomRow) =>
                    bottomRow switch
                    {
                        "|_|" => Digit.Zero + Digit.Six + Digit.Eight,
                        "  |" => Digit.One + Digit.Four + Digit.Seven,
                        "|_ " => Digit.Two,
                        " _|" => Digit.Three + Digit.Five + Digit.Nine,
                        _ => throw new ArgumentException($"Invalid pattern [{bottomRow}]", nameof(bottomRow))
                    };
            }
        }
    }

    internal static class Output
    {
        public delegate string Writer(string text, object?[]? args = null);

        public static string WriteLineToConsole(this string text, params object?[]? args)
        {
            Console.WriteLine(text, args);
            return text;
        }

        public static string WriteToNull(this string text, params object?[]? _) => text;

        public static Maybe<string> ReportOnFilename(this Maybe<string> maybeFilename, Writer successWriter, Writer failureWriter)
        {
            const string haveFilenameReport = "Attempting to read file: {0}";
            const string emptyFilenameReport = "ERROR: No file name given.";

            return maybeFilename
                .HaveThen((filename) => successWriter(haveFilenameReport, new [] { filename }))
                .EmptyThen(() => failureWriter(emptyFilenameReport));
        }

        public static Maybe<IEnumerable<string>> ReportOnFile(this Maybe<IEnumerable<string>> maybeLines, Writer successWriter, Writer failureWriter)
        {
            const string haveLinesReport = "File opened.";
            const string emptyLinesReport = "ERROR: Could not open file.";

            return maybeLines
                .HaveThen(() => successWriter(haveLinesReport))
                .EmptyThen(() => failureWriter(emptyLinesReport));
        }
    }

    public static class Program
    {
        public static Maybe<string> ToFilename(this string[] args) =>
            Fn.Try<string>
            (
                () => args[0],
                Fn.Handler<IndexOutOfRangeException>(() => true)
            )
            .Bind(ToValidFilename);

        public static Maybe<string> ToValidFilename(this string filename) =>
            string.IsNullOrWhiteSpace(filename) ?
                Maybe<string>.None :
                Path.GetFullPath(filename);

        public static Maybe<TextReader> OpenFile(string filename, Func<bool> invalidFileNameHandler) =>
            Fn.Try<TextReader>
            (
                () => new StreamReader(filename),
                Fn.Handler<FileNotFoundException>(invalidFileNameHandler),
                Fn.Handler<DirectoryNotFoundException>(invalidFileNameHandler),
                Fn.Handler<IOException>(invalidFileNameHandler)
            );

        public static int Main(string[] args)
        {
            const int StatusSuccess = 0;
            const int StatusError = 1;

            int status = StatusSuccess;

            Output.Writer successWriter = Output.WriteToNull;
            Output.Writer failureWriter = Output.WriteLineToConsole;
            Output.Writer outputWriter = Output.WriteLineToConsole;

            // User Story 1: args -> displayed list of account numbers
            Maybe<string> maybeFilename =
                args
                    .ToFilename()
                    .ReportOnFilename(successWriter, failureWriter);

            if (!maybeFilename)
                return StatusError;

            Maybe<IEnumerable<string>> maybeLines =
                maybeFilename
                    .Bind(FileReader.ToLines)
                    .ReportOnFile(successWriter, failureWriter);

            if (!maybeLines)
                return StatusError;

            Maybe<IEnumerable<Account>> maybeAccounts =
                maybeLines
                    .Map(FileReader.ToAccounts)
                    // * display account numbers
                    ;

            return status;
        }
    }
}
