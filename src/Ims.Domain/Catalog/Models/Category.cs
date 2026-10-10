namespace Ims.Domain;

public class Category : Entity
{
    private Category() { }

    public string Name { get; private set; } = "";

    public static Category Create(string name)
    {
        var c = new Category();
        c.Rename(name);
        return c;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(Messages.CategoryNameRequired);
        Name = name.Trim();
    }
}
