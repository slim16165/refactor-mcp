public class A
{
    public class Nested { }

    public Nested GetNested()
    {
        return new Nested();
    }
}

public class B { }