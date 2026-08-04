// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections.Generic;
using System.Linq;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

public partial class EnrichedBlockViewModel : ObservableObject
{
    private readonly EnrichedBlockResult _result;

    // Cached filtered collections (computed once in constructor)
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> ValidEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> InvalidEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> EnrichedEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> LocationEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> PersonEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> OrganizationEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> DateEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> NumberEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> FacilityEntities { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<EnrichedEntityEntry> OtherEntities { get; }

    public EnrichedBlockViewModel(EnrichedBlockResult result)
    {
        _result = result;
        // Cache all filtered collections once to avoid repeated LINQ queries on UI binding
        ValidEntities = _result.Entities.Where(e => e.ValidationStatus == "valid").ToList().AsReadOnly();
        InvalidEntities = _result.Entities.Where(e => e.ValidationStatus == "invalid").ToList().AsReadOnly();
        EnrichedEntities = _result.Entities.Where(e => !string.IsNullOrEmpty(e.EnrichmentSource)).ToList().AsReadOnly();
        LocationEntities = _result.Entities.Where(e => e.Category == "LOCATION").ToList().AsReadOnly();
        PersonEntities = _result.Entities.Where(e => e.Category == "PERSON").ToList().AsReadOnly();
        OrganizationEntities = _result.Entities.Where(e => e.Category == "ORGANIZATION").ToList().AsReadOnly();
        DateEntities = _result.Entities.Where(e => e.Category == "DATE").ToList().AsReadOnly();
        NumberEntities = _result.Entities.Where(e => e.Category == "NUMBER").ToList().AsReadOnly();
        FacilityEntities = _result.Entities.Where(e => e.Category == "FACILITY").ToList().AsReadOnly();
        OtherEntities = _result.Entities.Where(e =>
            e.Category != "LOCATION" && e.Category != "PERSON" && e.Category != "ORGANIZATION" &&
            e.Category != "DATE" && e.Category != "NUMBER" && e.Category != "FACILITY").ToList().AsReadOnly();
    }

    public List<int> BlockIds => _result.BlockIds;
    public List<EnrichedEntityEntry> AllEntities => _result.Entities;
    public List<EnrichedRelation> Relations => _result.Relations;

    public int EntityCount => _result.Entities.Count;
    public int ValidCount => ValidEntities.Count;
    public int InvalidCount => InvalidEntities.Count;
    public int EnrichedCount => EnrichedEntities.Count;
    public int RelationCount => _result.Relations.Count;
    public int LocationCount => LocationEntities.Count;
    public int PersonCount => PersonEntities.Count;
    public int OrganizationCount => OrganizationEntities.Count;
    public int DateCount => DateEntities.Count;
    public int NumberCount => NumberEntities.Count;
    public int FacilityCount => FacilityEntities.Count;
    public int OtherCount => OtherEntities.Count;
    public bool HasEntities => _result.Entities.Count > 0;
    public bool HasEnriched => EnrichedCount > 0;
    public bool HasInvalid => InvalidCount > 0;
    public bool HasRelations => _result.Relations.Count > 0;

    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{EntityCount} entities" };
            if (EnrichedCount > 0) parts.Add($"{EnrichedCount} enriched");
            if (InvalidCount > 0) parts.Add($"{InvalidCount} invalid");
            if (RelationCount > 0) parts.Add($"{RelationCount} relations");
            return string.Join(", ", parts);
        }
    }

    public string BlockLabel
    {
        get
        {
            var ids = _result.BlockIds;
            if (ids.Count == 0) return "block ?";
            if (ids.Count == 1) return $"block {ids[0]}";
            return $"blocks {ids[0]}-{ids[^1]}";
        }
    }
}
