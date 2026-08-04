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

using Caly.Core.Models;
using Caly.Core.Services;

namespace Caly.Tests;

public class LocationRelationCollectionTests
{
    [Fact]
    public void FindParentOf_ReturnsCorrectParent()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "北京",
            TargetLocation = "北三环",
            DepRel = "nn"
        });

        var parent = coll.FindParentOf("北三环");
        Assert.NotNull(parent);
        Assert.Equal("北京", parent.SourceLocation);
        Assert.Equal(LocationRelationType.ParentChild, parent.RelationType);
    }

    [Fact]
    public void FindParentOf_NotFound_ReturnsNull()
    {
        var coll = new LocationRelationCollection();
        Assert.Null(coll.FindParentOf("不存在的"));
    }

    [Fact]
    public void FindOwnerOf_ReturnsCorrectOwner()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Ownership,
            SourceLocation = "海淀区",
            TargetLocation = "五道口",
            DepRel = "assmod"
        });

        var owner = coll.FindOwnerOf("五道口");
        Assert.NotNull(owner);
        Assert.Equal("海淀区", owner.SourceLocation);
    }

    [Fact]
    public void FindContextProvider_ReturnsParentOrOwner()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "北京",
            TargetLocation = "北三环",
        });
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Ownership,
            SourceLocation = "海淀区",
            TargetLocation = "五道口",
        });

        var ctx1 = coll.FindContextProvider("北三环");
        Assert.Equal("北京", ctx1?.SourceLocation);

        var ctx2 = coll.FindContextProvider("五道口");
        Assert.Equal("海淀区", ctx2?.SourceLocation);
    }

    [Fact]
    public void FindParallels_ReturnsBothSides()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Parallel,
            SourceLocation = "北京",
            TargetLocation = "上海",
            DepRel = "conj"
        });

        var parallels1 = coll.FindParallels("北京");
        Assert.Single(parallels1);
        Assert.Equal("上海", parallels1[0].TargetLocation);

        var parallels2 = coll.FindParallels("上海");
        Assert.Single(parallels2);
        Assert.Equal("北京", parallels2[0].SourceLocation);
    }

    [Fact]
    public void FindCanonicalName_ReturnsCanonical()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Coreference,
            SourceLocation = "帝都",
            TargetLocation = "北京",
        });

        var canonical = coll.FindCanonicalName("帝都");
        Assert.Equal("北京", canonical);
    }

    [Fact]
    public void GetRelatedLocations_ReturnsAllRelated()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "北京",
            TargetLocation = "北三环",
        });
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Parallel,
            SourceLocation = "北京",
            TargetLocation = "上海",
        });

        var related = coll.GetRelatedLocations("北京");
        Assert.Equal(2, related.Count);
        Assert.Contains("北三环", related);
        Assert.Contains("上海", related);
    }

    [Fact]
    public void HasRelations_EmptyCollection_ReturnsFalse()
    {
        var coll = new LocationRelationCollection();
        Assert.False(coll.HasRelations);
    }

    [Fact]
    public void HasRelations_WithRelations_ReturnsTrue()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "北京",
            TargetLocation = "西二旗",
        });
        Assert.True(coll.HasRelations);
    }
}

public class GeocodingEnhancementStrategyTests
{
    [Fact]
    public void DecideStrategy_NoRelations_ReturnsNone()
    {
        var entity = new SemanticEntity
        {
            Text = "北京",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Subject
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, null);
        Assert.Equal(QueryEnhancement.None, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.None, strategy.ResultFiltering);
    }

    [Fact]
    public void DecideStrategy_AttributiveWithParent_ReturnsSpatialBiasAndStrictFilter()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "北京",
            TargetLocation = "北三环",
        });

        var entity = new SemanticEntity
        {
            Text = "北三环",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Attributive
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, coll);
        Assert.Equal(QueryEnhancement.SpatialBias, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.StrictParentFilter, strategy.ResultFiltering);
        Assert.Equal("北京", strategy.ContextLocationName);
        Assert.True(strategy.DependsOnParent);
    }

    [Fact]
    public void DecideStrategy_ObjectWithOwner_ReturnsTextAugmentAndAreaBoost()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Ownership,
            SourceLocation = "海淀区",
            TargetLocation = "五道口",
        });

        var entity = new SemanticEntity
        {
            Text = "五道口",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Object
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, coll);
        Assert.Equal(QueryEnhancement.TextAugment, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.ParentAreaBoost, strategy.ResultFiltering);
        Assert.Equal("海淀区", strategy.ContextLocationName);
        Assert.False(strategy.DependsOnParent);
    }

    [Fact]
    public void DecideStrategy_AdverbialWithParent_ReturnsTextAugmentAndAreaBoost()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.ParentChild,
            SourceLocation = "长安",
            TargetLocation = "未央宫",
        });

        var entity = new SemanticEntity
        {
            Text = "未央宫",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Adverbial
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, coll);
        Assert.Equal(QueryEnhancement.TextAugment, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.ParentAreaBoost, strategy.ResultFiltering);
        Assert.False(strategy.DependsOnParent);
    }

    [Fact]
    public void DecideStrategy_Coreference_ReturnsNormalization()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Coreference,
            SourceLocation = "帝都",
            TargetLocation = "北京",
        });

        var entity = new SemanticEntity
        {
            Text = "帝都",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Subject
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, coll);
        Assert.Equal(QueryEnhancement.TextAugment, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.None, strategy.ResultFiltering);
        Assert.Equal("北京", strategy.ContextLocationName);
    }

    [Fact]
    public void DecideStrategy_Parallel_ReturnsNone()
    {
        var coll = new LocationRelationCollection();
        coll.Relations.Add(new LocationRelation
        {
            RelationType = LocationRelationType.Parallel,
            SourceLocation = "北京",
            TargetLocation = "上海",
        });

        var entity = new SemanticEntity
        {
            Text = "北京",
            Category = "LOCATION",
            SyntacticRole = LocationSyntacticRole.Subject
        };

        var strategy = GeocodingEnhancementStrategy.DecideStrategy(entity, coll);
        Assert.Equal(QueryEnhancement.None, strategy.QueryEnhancement);
        Assert.Equal(ResultFiltering.None, strategy.ResultFiltering);
    }

    [Theory]
    [InlineData(LocationSyntacticRole.Attributive, 0.3, -0.3)]
    [InlineData(LocationSyntacticRole.Object, 0.3, -0.2)]
    [InlineData(LocationSyntacticRole.Predicative, 0.3, -0.2)]
    [InlineData(LocationSyntacticRole.Adverbial, 0.2, -0.1)]
    [InlineData(LocationSyntacticRole.Subject, 0.2, -0.1)]
    [InlineData("Unknown", 0.15, -0.1)]
    public void GetConfidenceAdjustment_ReturnsCorrectValues(string role, double expectedBoost, double expectedPenalty)
    {
        var (boost, penalty) = GeocodingEnhancementStrategy.GetConfidenceAdjustment(role);
        Assert.Equal(expectedBoost, boost);
        Assert.Equal(expectedPenalty, penalty);
    }
}

public class BuildEnhancedQueryTests
{
    [Theory]
    [InlineData("北三环", "北京", "北京 北三环")]
    [InlineData("五道口", "海淀区", "海淀区 五道口")]
    [InlineData("长安街", null, "长安街")]
    [InlineData("长安街", "", "长安街")]
    public void BuildEnhancedQuery_BuildsCorrectly(string name, string? context, string expected)
    {
        var result = GeocodingEnhancementStrategy.BuildEnhancedQuery(name, context ?? "");
        Assert.Equal(expected, result);
    }
}
