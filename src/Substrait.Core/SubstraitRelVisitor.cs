using Substrait.Relation;

namespace Substrait.Core
{
  /// <summary>
  /// Visitor to transform, compile, and/or process SQL logical operators represented using Substrait. The visitor has
  /// methods for visiting relation operator objects as input and provides concrete implementation to meet its goals.
  /// </summary>
  public abstract class SubstraitRelVisitor
  {
    /// <summary>
    /// Visit relational operator of type AGGREGATE
    /// </summary>
    public void Visit(Aggregate aggregate)
    {
      if (aggregate is null)
      {
        throw new ArgumentNullException(nameof(aggregate));
      }

      Fallback(aggregate);
    }

    /// <summary>
    /// Visit relational operator of type FETCH
    /// </summary>
    public void Visit(Fetch fetch)
    {
      if (fetch is null)
      {
        throw new ArgumentNullException(nameof(fetch));
      }

      Fallback(fetch);
    }

    /// <summary>
    /// Visit relational operator of type FILTER
    /// </summary>
    public void Visit(Filter filter)
    {
      if (filter is null)
      {
        throw new ArgumentNullException(nameof(filter));
      }

      Fallback(filter);
    }

    /// <summary>
    /// Visit relational operator of type JOIN
    /// </summary>
    public void Visit(Join join)
    {
      if (join is null)
      {
        throw new ArgumentNullException(nameof(join));
      }

      Fallback(join);
    }

    /// <summary>
    /// Visit relational operator of type PROJECT
    /// </summary>
    public void Visit(Project project)
    {
      if (project is null)
      {
        throw new ArgumentNullException(nameof(project));
      }

      Fallback(project);
    }

    /// <summary>
    /// Visit relational operator of type READ
    /// </summary>
    public void Visit(Read read)
    {
      if (read is null)
      {
        throw new ArgumentNullException(nameof(read));
      }

      Fallback(read);
    }

    /// <summary>
    /// Visit relational operator of type SORT
    /// </summary>
    public void Visit(Sort sort)
    {
      if (sort is null)
      {
        throw new ArgumentNullException(nameof(sort));
      }

      Fallback(sort);
    }

    public void Fallback(Rel _)
    {
      throw new InvalidOperationException();
    }
  }
}
