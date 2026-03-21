using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class HungarianAlgorithmTests
{
    [Test]
    public void Solve_EmptyMatrix_ReturnsEmpty()
    {
        var scores = new double[0, 0];
        var result = HungarianAlgorithm.Solve(scores, 0.6);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Solve_SinglePairAboveThreshold_ReturnsAssignment()
    {
        var scores = new double[1, 1];
        scores[0, 0] = 0.9;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Row, Is.EqualTo(0));
        Assert.That(result[0].Col, Is.EqualTo(0));
        Assert.That(result[0].Score, Is.EqualTo(0.9));
    }

    [Test]
    public void Solve_SinglePairBelowThreshold_ReturnsEmpty()
    {
        var scores = new double[1, 1];
        scores[0, 0] = 0.3;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Solve_TwoByTwo_OptimalAssignment()
    {
        // Row 0 prefers Col 1 (0.9 vs 0.7)
        // Row 1 prefers Col 0 (0.85 vs 0.6)
        // Optimal: Row0->Col1, Row1->Col0 (total 1.75)
        // Greedy would pick Row0->Col1 (0.9), Row1->Col0 (0.85) — same here
        var scores = new double[2, 2];
        scores[0, 0] = 0.7;
        scores[0, 1] = 0.9;
        scores[1, 0] = 0.85;
        scores[1, 1] = 0.6;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(2));
        var byRow = result.ToDictionary(r => r.Row, r => r.Col);
        Assert.That(byRow[0], Is.EqualTo(1));
        Assert.That(byRow[1], Is.EqualTo(0));
    }

    [Test]
    public void Solve_HungarianBeatsGreedy_ConflictingPreferences()
    {
        // Both rows prefer Col 0.
        // Greedy picks Row0->Col0 (0.95), then Row1 gets Col1 (0.5, below threshold).
        // Hungarian: Row0->Col1 (0.8), Row1->Col0 (0.9) = total 1.7, both above threshold.
        var scores = new double[2, 2];
        scores[0, 0] = 0.95;
        scores[0, 1] = 0.8;
        scores[1, 0] = 0.9;
        scores[1, 1] = 0.5;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(2));
        var byRow = result.ToDictionary(r => r.Row, r => r.Col);
        Assert.That(byRow[0], Is.EqualTo(1)); // Col 1
        Assert.That(byRow[1], Is.EqualTo(0)); // Col 0
    }

    [Test]
    public void Solve_RectangularMatrix_MoreRowsThanCols()
    {
        // 3 rows, 2 cols — one row will be unmatched
        var scores = new double[3, 2];
        scores[0, 0] = 0.9;
        scores[0, 1] = 0.3;
        scores[1, 0] = 0.3;
        scores[1, 1] = 0.85;
        scores[2, 0] = 0.4;
        scores[2, 1] = 0.4;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(2));
        var byRow = result.ToDictionary(r => r.Row, r => r.Col);
        Assert.That(byRow[0], Is.EqualTo(0));
        Assert.That(byRow[1], Is.EqualTo(1));
    }

    [Test]
    public void Solve_RectangularMatrix_MoreColsThanRows()
    {
        // 2 rows, 3 cols — one col will be unmatched
        var scores = new double[2, 3];
        scores[0, 0] = 0.3;
        scores[0, 1] = 0.9;
        scores[0, 2] = 0.4;
        scores[1, 0] = 0.85;
        scores[1, 1] = 0.3;
        scores[1, 2] = 0.4;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(2));
        var byRow = result.ToDictionary(r => r.Row, r => r.Col);
        Assert.That(byRow[0], Is.EqualTo(1));
        Assert.That(byRow[1], Is.EqualTo(0));
    }

    [Test]
    public void Solve_AllBelowThreshold_ReturnsEmpty()
    {
        var scores = new double[2, 2];
        scores[0, 0] = 0.3;
        scores[0, 1] = 0.4;
        scores[1, 0] = 0.5;
        scores[1, 1] = 0.2;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Solve_ThreeByThree_OptimalAssignment()
    {
        var scores = new double[3, 3];
        scores[0, 0] = 0.9;
        scores[0, 1] = 0.1;
        scores[0, 2] = 0.1;
        scores[1, 0] = 0.1;
        scores[1, 1] = 0.9;
        scores[1, 2] = 0.1;
        scores[2, 0] = 0.1;
        scores[2, 1] = 0.1;
        scores[2, 2] = 0.9;

        var result = HungarianAlgorithm.Solve(scores, 0.6);

        Assert.That(result, Has.Count.EqualTo(3));
        var byRow = result.ToDictionary(r => r.Row, r => r.Col);
        Assert.That(byRow[0], Is.EqualTo(0));
        Assert.That(byRow[1], Is.EqualTo(1));
        Assert.That(byRow[2], Is.EqualTo(2));
    }
}
