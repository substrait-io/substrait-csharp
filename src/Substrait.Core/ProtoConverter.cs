using Substrait.Expression;
using Substrait.Protobuf;
using Substrait.Relation;
using System.Collections.Immutable;
using ProtoRel = Substrait.Protobuf.Rel;
using ProtoRex = Substrait.Protobuf.Expression;
using Rel = Substrait.Relation.Rel;

namespace Substrait.Core
{
  /// <summary>
  /// Utility to convert Substrait protobuf messages to/from charp domain-objects.
  /// </summary>
  public class ProtoConverter
  {
    /// <summary>
    /// Create domain object from a protobuf encoded message
    /// </summary>
    /// <param name="protoRel">Relation encoded as protobuf message</param>
    /// <returns><see cref="Rel"/> corresponding to message's relation</returns>
    public Rel ToRel(ProtoRel protoRel)
    {
      var relType = protoRel.RelTypeCase;

      return relType switch
      {
        ProtoRel.RelTypeOneofCase.Aggregate => CreateAgg(protoRel.Aggregate),
        ProtoRel.RelTypeOneofCase.Cross => CreateCross(protoRel.Cross),
        ProtoRel.RelTypeOneofCase.Fetch => CreateFetch(protoRel.Fetch),
        ProtoRel.RelTypeOneofCase.Filter => CreateFilter(protoRel.Filter),
        ProtoRel.RelTypeOneofCase.Join => CreateJoin(protoRel.Join),
        ProtoRel.RelTypeOneofCase.Project => CreateProject(protoRel.Project),
        ProtoRel.RelTypeOneofCase.Read => CreateRead(protoRel.Read),
        ProtoRel.RelTypeOneofCase.Sort => CreateSort(protoRel.Sort),
        _ => throw new NotImplementedException(relType.ToString()),
      };
    }

    /// <summary>
    /// Create domain object specific to relational expression type from protobuf message
    /// </summary>
    /// <param name="protoRex">Relational expression encoded as protobuf message</param>
    /// <returns><see cref="Rel"/> corresponding to message's expression type</returns>
    private RelExpression ToExpression(ProtoRex protoRex)
    {
      var rexType = protoRex.RexTypeCase;

      return rexType switch
      {
        ProtoRex.RexTypeOneofCase.Selection => CreateFieldReference(protoRex.Selection),
        ProtoRex.RexTypeOneofCase.None => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.Literal => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.ScalarFunction => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.WindowFunction => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.IfThen => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.SwitchExpression => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.SingularOrList => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.MultiOrList => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.Cast => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.Subquery => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.Nested => throw new NotImplementedException(),
        ProtoRex.RexTypeOneofCase.Enum => throw new NotImplementedException(),
        _ => throw new NotImplementedException(rexType.ToString()),
      };
    }

    private Rel CreateAgg(AggregateRel agg)
    {
      var input = ToRel(agg.Input);
      var inputs = ImmutableList.Create(input);
      return new Aggregate()
      {
        Inputs = inputs,
      };
    }

    private Rel CreateCross(CrossRel cross)
    {
      var left = ToRel(cross.Left);
      var right = ToRel(cross.Right);
      var inputs = ImmutableList.Create(left, right);
      return new Cross()
      {
        Inputs = inputs,
      };
    }

    private Rel CreateFetch(FetchRel fetch)
    {
      var input = ToRel(fetch.Input);
      var inputs = ImmutableList.Create(input);
      return new Fetch()
      {
        Count = fetch.Count,
        Inputs = inputs,
      };
    }

    private Rel CreateFilter(FilterRel filter)
    {
      var input = ToRel(filter.Input);
      var inputs = ImmutableList.Create(input);
      return new Filter()
      {
        Inputs = inputs,
      };
    }

    private Rel CreateJoin(JoinRel join)
    {
      var left = ToRel(join.Left);
      var right = ToRel(join.Right);
      var _ = ToExpression(join.Expression);
      var inputs = ImmutableList.Create(left, right);
      return new Join()
      {
        Inputs = inputs,
      };
    }

    private Rel CreateProject(ProjectRel project)
    {
      var input = ToRel(project.Input);
      var inputs = ImmutableList.Create(input);
      return new Join()
      {
        Inputs = inputs,
      };
    }

    private Rel CreateRead(ReadRel read)
    {
      return new Read()
      {
        Inputs = ImmutableList<Rel>.Empty,
      };
    }

    private Rel CreateSort(SortRel sort)
    {
      var input = ToRel(sort.Input);
      var inputs = ImmutableList.Create(input);
      return new Sort()
      {
        Inputs = inputs,
      };
    }

    private RelExpression CreateFieldReference(ProtoRex.Types.FieldReference field)
    {
      return new FieldReference();
    }
  }
}
