using Substrait.Expression;
using Substrait.Protobuf;

namespace Substrait.Relation
{
  /// <summary>
  /// The binary JOIN relational operator, <see cref="Protobuf.JoinRel"/>
  /// </summary>
  public class Join : Rel
  {
    public RelExpression? Condition { get; init; }
    public RelExpression? PostJoinFilter { get; init; }
    public JoinType JoinType { get; init; } = JoinType.UNKNOWN;

    public override void Accept(SubstraitRelVisitor visitor)
    {
      base.Accept(visitor);
      Condition?.Accept(visitor);
      PostJoinFilter?.Accept(visitor);
    }
  }

  public enum JoinType
  {
    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Anti"/>
    /// </summary>
    ANTI,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Inner"/>
    /// </summary>
    INNER,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Left"/>
    /// </summary>
    LEFT,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Outer"/>
    /// </summary>
    OUTER,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Right"/>
    /// </summary>
    RIGHT,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Semi"/>
    /// </summary>
    SEMI,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Single"/>
    /// </summary>
    SINGLE,

    /// <summary>
    /// <see cref="JoinRel.Types.JoinType.Unspecified"/>
    /// </summary>
    UNKNOWN
  }
}
