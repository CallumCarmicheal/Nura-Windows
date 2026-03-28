using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuraLib.Utilities.Docs;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
internal sealed class BluetoothImplementationRequiredAttribute : Attribute {
    public BluetoothImplementationRequiredAttribute(string area) {
        Area = area;
    }

    public string Area { get; }

    public string? Notes { get; init; }
}