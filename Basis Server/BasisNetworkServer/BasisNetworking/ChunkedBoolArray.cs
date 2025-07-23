using Basis.Network.Core;
using System;
public class LockedBoolArray
{
    private readonly bool[] _array;
    private readonly object[] _locks;
    private readonly int _totalSize;

    public LockedBoolArray()
    {
        if (BasisNetworkCommons.MaxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(BasisNetworkCommons.MaxConnections), "Total size must be greater than zero.");

        _totalSize = BasisNetworkCommons.MaxConnections;
        _array = new bool[_totalSize];
        _locks = new object[_totalSize];
        for (int i = 0; i < _totalSize; i++)
        {
            _locks[i] = new object();
        }
    }

    public void SetBool(int index, bool value)
    {
        lock (_locks[index])
        {
            _array[index] = value;
        }
    }

    public bool GetBool(int index)
    {
        lock (_locks[index])
        {
            return _array[index];
        }
    }
}
