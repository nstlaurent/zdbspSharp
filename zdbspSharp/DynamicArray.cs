namespace zdbspSharp;

public class DynamicArray<T>
{

    public int Length { get; set; }

    public T[] Data { get; private set; }

    public int Capacity => Data.Length;


    public DynamicArray(int capacity = 8)
    {
        Data = new T[Math.Max(1, capacity)];
    }

    public T this[int index]
    {
        get => Data[index];
        set => Data[index] = value;
    }

    public void Clear()
    {
        Length = 0;
    }

    public bool Contains(T element)
    {
        for (int i = 0; i < Length; i++)
            if (Equals(Data[i], element))
                return true;
        return false;
    }

    public void Add(T element)
    {
        if (Length == Capacity)
            SetCapacity(Capacity * 2);

        Data[Length++] = element;
    }

    public void Add(params T[] elements)
    {
        EnsureCapacity(Length + elements.Length);

        if (elements.Length < 10)
        {
            for (int i = 0; i < elements.Length; i++)
                Data[Length + i] = elements[i];
        }
        else
        {
            Array.Copy(elements, 0, Data, Length, elements.Length);
        }

        Length += elements.Length;
    }

    public void Add(T[] elements, int length)
    {
        EnsureCapacity(Length + length);

        if (length < 10)
        {
            for (int i = 0; i < length; i++)
                Data[Length + i] = elements[i];
        }
        else
        {
            Array.Copy(elements, 0, Data, Length, length);
        }

        Length += length;
    }

    public void AddRange(IList<T> elements)
    {
        EnsureCapacity(Length + elements.Count);

        for (int i = 0; i < elements.Count; i++)
            Data[Length + i] = elements[i];

        Length += elements.Count;
    }

    public void AddRange(Span<T> elements)
    {
        EnsureCapacity(Length + elements.Length);

        for (int i = 0; i < elements.Length; i++)
            Data[Length + i] = elements[i];

        Length += elements.Length;
    }

    public void AddRange(DynamicArray<T> elements)
    {
        EnsureCapacity(Length + elements.Length);

        for (int i = 0; i < elements.Length; i++)
            Data[Length + i] = elements[i];

        Length += elements.Length;
    }

    public void Resize(int size)
    {
        SetCapacity(size);
        Length = size;
    }

    public void SetLength(int length)
    {
        if (Length > length)
        {
            Resize(length);
            return;
        }

        Length = length;
    }

    public void Reserve(int amount)
    {
        Resize(Length + amount);
    }

    public T RemoveLast()
    {
        if (Length == 0)
            throw new InvalidOperationException("No data to remove.");
        T data = Data[Length - 1];
        Length--;
        return data;
    }

    public void EnsureCapacity(int desiredCapacity)
    {
        if (desiredCapacity <= Capacity)
            return;

        int newCapacity = Capacity;
        if (desiredCapacity >= int.MaxValue / 2)
            newCapacity = int.MaxValue;
        else
            while (newCapacity < desiredCapacity)
                newCapacity *= 2;

        SetCapacity(newCapacity);
    }

    private void SetCapacity(int newCapacity)
    {
        if (newCapacity <= Capacity)
            return;

        T[] newData = new T[newCapacity];
        Array.Copy(Data, newData, Data.Length);
        Data = newData;
    }
}
