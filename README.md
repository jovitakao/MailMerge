MailMerge for docx Documents
============================

CommandLine Usage
-----------------

    dotnet MailMerge.dll inputFile1 outputFile1 [[inputFileN outputFileN]...] [ key=value[...] ]

Example

    dotnet MailMerge.dll input1.docx output1Bill.docx  FirstName=Bill  "LastName=O Reilly"



Component Usage
---------------

    (outputStream, errors) = new MailMerge().Merge(inputStream, Dictionary);

    (bool, errors) = new MailMerge().Merge(inputFileName, Dictionary, outputFileName);
        

MailMerge does not use any desktop automation components, and should be suitable for serverside use. 

TODO
----
Overloads for multiline datasources: Lists, CSV files & .xmlx files.
Platform executables
