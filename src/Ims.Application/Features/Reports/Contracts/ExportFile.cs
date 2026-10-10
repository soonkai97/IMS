namespace Ims.Application.Features.Reports;

public record ExportFile(byte[] Content, string FileName, string ContentType);
