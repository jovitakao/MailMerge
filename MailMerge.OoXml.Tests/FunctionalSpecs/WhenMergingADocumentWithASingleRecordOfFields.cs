﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MailMerge.Helpers;
using NUnit.Framework;
using TestBase;

namespace MailMerge.OoXml.Tests.FunctionalSpecs
{
    [TestFixture]
    public class WhenMergingADocumentWithASingleRecordOfFields
    {
        MailMerge sut;
        const string TemplateDocx = "TestDocuments\\ATemplate.docx";

        Dictionary<string, string> MergeFieldsForTemplateDocx = new Dictionary<string, string>
        {
            {"FirstName","FakeFirst"},
            {"LastName","FakeLast"}
        };

        [SetUp]
        public void Setup()
        {
            sut = new MailMerge(Startup.Configure().CreateLogger(GetType()), Startup.Settings);
        }

        [Test]
        public void Returns_TheDocumentWithMergeFieldsReplaced()
        {
            Stream output = null; AggregateException exceptions;

            using (var original = new FileStream(TemplateDocx, FileMode.Open, FileAccess.Read, FileShare.Read))
                try
                {

                    (output, exceptions) = sut.Merge(TemplateDocx, MergeFieldsForTemplateDocx);

                    if (exceptions.InnerExceptions.Any()) { throw exceptions; }

                    var outputText = output.AsWordprocessingDocument(false).MainDocumentPart.Document.InnerText;

                    MergeFieldsForTemplateDocx
                        .Values
                        .ShouldAll(v => outputText.ShouldContain(v, "Didn't find merge value in output text"));

                    MergeFieldsForTemplateDocx
                        .Keys
                        .ShouldAll(k => outputText.ShouldNotContain("«" + k + "»", "Found mergefield name in output text"));

                    MergeFieldsForTemplateDocx
                        .Keys
                    .ShouldAll(k => output.AsWordprocessingDocument().MainDocumentPart.Document.OuterXml.ShouldContain($"MERGEFIELD {k}"));

                }
                finally
                {
                    output?.Dispose();
                }
        }

        [OneTimeSetUp]
        public void EnsureTestDependencies()
        {
            File.Exists(TemplateDocx).ShouldBeTrue($"TestDependency: \"{TemplateDocx}\" should be marked as CopyToOutputDirectory but didn't find it at {new FileInfo(TemplateDocx).FullName}");
        }

    }
}
