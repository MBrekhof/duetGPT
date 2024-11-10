using DevExpress.Pdf;
using DevExpress.XtraRichEdit;
using System.Text;

namespace duetGPT.Components.Pages
{
    public partial class Claude
    {
        private string ExtractTextFromPdf(byte[] pdfContent)
        {
            using (var pdfDocumentProcessor = new PdfDocumentProcessor())
            using (var stream = new MemoryStream(pdfContent)) // Convert byte array to stream
            {
                pdfDocumentProcessor.LoadDocument(stream);
                var text = new StringBuilder();

                for (int i = 0; i < pdfDocumentProcessor.Document.Pages.Count; i++)
                {
                    text.Append(pdfDocumentProcessor.GetPageText(i));
                }

                return text.ToString();
            }
        }

        private string ExtractTextFromDocx(byte[] docxContent)
        {
            using var richEditDocumentServer = new RichEditDocumentServer();
            richEditDocumentServer.LoadDocument(docxContent, DocumentFormat.OpenXml);
            return richEditDocumentServer.Text;
        }

        private string ExtractTextFromDoc(byte[] docxContent)
        {
            using var richEditDocumentServer = new RichEditDocumentServer();
            richEditDocumentServer.LoadDocument(docxContent, DocumentFormat.Doc);
            return richEditDocumentServer.Text;
        }
    }
}
