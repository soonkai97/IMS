namespace Ims.Application;

/// <summary>The two ids of one row: the integer key the database uses and the public id a client sees.</summary>
public readonly record struct IdPair(int Id, Guid PublicId);
