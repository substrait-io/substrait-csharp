using Substrait.Relation;

namespace Substrait.Expression
{
  /// <summary>
  /// Base type for all relational expressions, <see cref="Protobuf.Expression"/>
  /// </summary>
  abstract public class RelExpression
  {
    public virtual void Accept(SubstraitRelVisitor visitor)
    {
    }
  }
}
