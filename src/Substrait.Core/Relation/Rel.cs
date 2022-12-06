namespace Substrait.Relation
{
  /// <summary>
  /// Base type for all relational operators, <see cref="Protobuf.Rel"/>
  /// </summary>
  abstract public class Rel
  {
    /// <summary>
    /// Input relations to this relation
    /// </summary>
    public IList<Rel> Inputs { get; init; } = new List<Rel>();

    public virtual void Accept(SubstraitRelVisitor visitor)
    {
      foreach (var input in Inputs)
      {
        input.Accept(visitor);
      }
    }
  }
}
