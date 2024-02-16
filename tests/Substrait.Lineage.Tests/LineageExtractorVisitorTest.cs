using Substrait.Protobuf;

namespace Substrait.Lineage.Tests
{
  [TestClass]
  public class LineageExtractorVisitorTest
  {
    [TestMethod]
    public void TestMethod1()
    {
      Plan plan;
      using var planPb = File.OpenRead(@"TestData\tpcds\Q02.pb");
      plan = Plan.Parser.ParseFrom(planPb);

      var lineageVisitor = new LineageExtractorRelVisitor();
      lineageVisitor.Execute(plan.Relations[0].Root.Input);
    }
  }
}