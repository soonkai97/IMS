namespace Ims.Application.Features.Reports;

public sealed record ExportFile(byte[] Content, string FileName, string ContentType);
