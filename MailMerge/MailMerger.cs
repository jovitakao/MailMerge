using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using MailMerge.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Settings = MailMerge.Properties.Settings;

namespace MailMerge
{
    /// <summary>
    /// A component for editing Word Docx files, and in particular for populating merge fields.
    /// </summary>
    public class MailMerger
    {
        public const string DATEKey = "DATE";
        internal readonly ILogger Logger;
        internal readonly Settings Settings;

        /// <summary>
        /// Use this property for DATE substitions if you require rudimentary WordProcessingML MergeFormat handling.
        /// Otherwise, a simpler choice is to add an entry with <code>Key=</code><seealso cref="DATEKey"/> to the fieldValues 
        /// passed in to <seealso cref="Merge(string,Dictionary{string,string})"/>
        /// </summary>
        public DateTime? DateTime { get; set; }

        public MailMerger(ILogger logger, Settings settings, DateTime? dateTime = null)
        {
            Logger = logger; Settings = settings; DateTime = dateTime;
        }
        
        /// <summary>Create a new MailMerger with Logger and Settings from <see cref="Startup"/></summary>
        public MailMerger()
        {
            Startup.Configure();
            Logger = Startup.CreateLogger<MailMerger>();
            Settings = Startup.Settings;
            DateTime=null;
        }

        /// <summary>
        /// Open the given <paramref name="inputDocxFileName"/> and merge fieldValues into it.
        /// </summary>
        /// <returns>
        /// Item1: The merged result as a new stream. If <paramref name="fieldValues"/> is empty, a <em>copy</em> of the original document is returned.
        /// Item2: An <see cref="AggregateException"/> containing any exceptions that were raised during the process.
        /// </returns>
        /// <param name="inputDocxFileName">Input file.</param>
        /// <param name="fieldValues">A dictionary keyed on mergefield names used in the document.</param>
        /// <remarks>Error handling: 
        /// You can inspect the returned <seealso cref="AggregateException.InnerExceptions"/> , or simply <code>throw</code it.</remarks>
        public (Stream, AggregateException) Merge(string inputDocxFileName, Dictionary<string, string> fieldValues)
        {
            var exception = ValidateParameterInputFile(inputDocxFileName);
            if(exception!=null){return (Stream.Null, new AggregateException(exception));}
            //
            using (var inputStream = new FileInfo(inputDocxFileName).OpenRead())
            {
                var (result,exceptions) = MergeInternal(inputStream, fieldValues);
                return (result, new AggregateException(exceptions));
            }
        }
        /// <summary>
        /// Open the given <paramref name="inputDocxFileName"/> and merge fieldValues into it. Save the result to <paramref name="outputDocxFileName"/>
        /// </summary>
        /// <returns>
        /// Item1: true if we saved an output document, false otherwise.
        /// Item2: An <see cref="AggregateException"/> containing any exceptions that were raised during the process.
        /// </returns>
        /// <param name="inputDocxFileName">Input file.</param>
        /// <param name="fieldValues">A dictionary keyed on mergefield names used in the document.</param>
        public (bool, AggregateException) Merge(string inputDocxFileName, Dictionary<string, string> fieldValues, string outputDocxFileName)
        {
            var exceptioni = ValidateParameterInputFile(inputDocxFileName);
            var exceptiono = ValidateParameterOutputFile(outputDocxFileName);
            if (exceptioni != null) { return (false, new AggregateException( new[] { exceptioni, exceptiono }.Where(e=>e!=null) )); }
            //
            try
            {
                using (var outstream = new FileInfo(outputDocxFileName).OpenWrite())
                using (var inputStream = new FileInfo(inputDocxFileName).Open(FileMode.Open, FileAccess.Read, FileShare.Read) )
                {
                    var (result, exceptions) = MergeInternal(inputStream, fieldValues);
                    result.Position = 0;
                    result.CopyTo(outstream);
                    return (true, new AggregateException(exceptions));
                }
            }
            catch(Exception e){ return (false, new AggregateException(e)); }
        }

        /// <summary>
        /// Open the given <paramref name="input"/> stream as a docx file, and merge fieldValues into it.
        /// </summary>
        /// <returns>
        /// Item1: The merged result as a new stream. If <paramref name="fieldValues"/> is empty, a <em>copy</em> of the original stream is returned.
        /// Item2: An <see cref="AggregateException"/> containing any exceptions that were raised during the process.
        /// </returns>
        /// <param name="input">Input stream of a docx file.</param>
        /// <param name="fieldValues">A dictionary keyed on mergefield names used in the document.</param>
        public (Stream,AggregateException) Merge(Stream input, Dictionary<string,string> fieldValues)
        {
            var (result,exceptions) = MergeInternal(input, fieldValues);
            return (result, new AggregateException(exceptions));
        }

        /// <summary>
        /// Open the given <paramref name="input"/> stream as a docx file, merges fieldValues into it, and saves the result to <paramref name="outputPath"/>
        /// </summary>
        /// <returns>
        /// Item1: The merged result as a new stream. If <paramref name="fieldValues"/> is empty, a <em>copy</em> of the original stream is returned.
        /// Item2: An <see cref="AggregateException"/> containing any exceptions that were raised during the process.
        /// </returns>
        /// <param name="input">Input stream of a docx file.</param>
        /// <param name="fieldValues">A dictionary keyed on mergefield names used in the document.</param>
        public (bool, AggregateException) Merge(Stream input, Dictionary<string,string> fieldValues, string outputPath)
        {
            var (result,exceptions) = MergeInternal(input, fieldValues);
            if (result != null) try
                {
                    using (var outstream = new FileInfo(outputPath).Create())
                    {
                        result.Position = 0;
                        result.CopyToAsync(outstream);
                        return (true, new AggregateException(exceptions));
                    }
                }
                catch (Exception e){ exceptions.Add(e); }
            return (false, new AggregateException(exceptions));
        }


        /// <summary>First copies the input stream to the output stream, then edits the outputstream to apply any mergefield transforms</summary>
        /// <returns>The edited output stream</returns>
        /// <param name="input">a docx file </param>
        /// <param name="fieldValues">A dictionary keyed on mergefield names used in the document.</param>
        (Stream, List<Exception>) MergeInternal(Stream input, Dictionary<string, string> fieldValues)
        {
            var exceptions = ValidateParameters(input, fieldValues);
            if (exceptions.Any() ) { return (Stream.Null, exceptions); }
            try
            {
                var outputStream= new MemoryStream(); 
                input.CopyTo(outputStream);                
                fieldValues = LogAndEnsureFieldValues(fieldValues, new Dictionary<string, string>());
                
                if (fieldValues.Any()){ApplyAllKnownMergeTransformationsToMainDocumentPart(fieldValues, outputStream);}
                
                return (outputStream, exceptions);
            }
            catch (Exception e){ exceptions.Add(e); }
            return (Stream.Null, exceptions);
        }

        internal void ApplyAllKnownMergeTransformationsToMainDocumentPart(Dictionary<string, string> fieldValues, Stream outputStream)
        {
            var xdoc = new XmlDocument(OoXmlNamespaces.Manager.NameTable);
            using(var wpDocx = WordprocessingDocument.Open(outputStream, false))
            using(var docOutStream = wpDocx.MainDocumentPart.GetStream(FileMode.Open,FileAccess.Read))
            {
                xdoc.Load(docOutStream);
            }
            
            xdoc.SimpleMergeFields(fieldValues, Logger);
            xdoc.ComplexMergeFields(fieldValues,Logger);
            xdoc.MergeDate(Logger,  DateTime, fieldValues.ContainsKey(DATEKey) ? fieldValues[DATEKey] : DateTime?.ToLongDateString());

            using (var wpDocx = WordprocessingDocument.Open(outputStream, true))
            {
                var bodyNode = xdoc.SelectSingleNode("/w:document/w:body", OoXmlNamespaces.Manager);
                var documentBody = new Body(bodyNode.OuterXml);
                wpDocx.MainDocumentPart.Document.Body = documentBody;
            }
        }

        Dictionary<string, string> LogAndEnsureFieldValues(Dictionary<string, string> fieldValues, Dictionary<string, string> @default)
        {
            if (fieldValues == null || fieldValues.Count == 0)
            {
                Logger.LogDebug("Starting Merge input stream with empty fieldValues={@fieldValues}", fieldValues);
            }
            else
            {
                Logger.LogTrace("Starting Merge input stream with fieldValues={@fieldValues}", fieldValues);
            }
            return fieldValues??@default;
        }


        List<Exception> ValidateParameters(Stream input, Dictionary<string, string> fieldValues)
        {
            var exceptions = new List<Exception>();
            if (input == null){exceptions.Add(new ArgumentNullException(nameof(input)));}
            if (fieldValues == null){exceptions.Add(new ArgumentNullException(nameof(fieldValues)));}
            return exceptions;
        }

        static Exception ValidateParameterInputFile(string inputFile)
        {
            try
            {
                if (inputFile == null) { return new ArgumentNullException(nameof(inputFile)); }
                if (!new FileInfo(inputFile).Exists) { return new FileNotFoundException("File Not Found: " + inputFile, inputFile); }
            }
            catch (Exception e) { return e; }

            return null;
        }

        static Exception ValidateParameterOutputFile(string outputFile)
        {
            try
            {
                if (outputFile == null) { return new ArgumentNullException(nameof(outputFile)); }
                var outputfileinfo = new FileInfo(outputFile);
                if (outputfileinfo.Exists) { return new IOException("File already exists : " + outputFile); }
                try { using (var test = outputfileinfo.OpenWrite()) { test.Close(); } } finally { outputfileinfo.Delete(); }
            }
            catch (Exception e) { return e; }

            return null;
        }
    }

    static class OoXmlWPValidator
    {
    }
}
