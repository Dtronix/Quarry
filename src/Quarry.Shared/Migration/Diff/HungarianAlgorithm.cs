using System;
using System.Collections.Generic;

namespace Quarry.Shared.Migration;

/// <summary>
/// Implements the Hungarian (Kuhn-Munkres) algorithm for optimal assignment
/// in a cost matrix. Used by rename detection to find the best global matching
/// between dropped and added tables/columns.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
static class HungarianAlgorithm
{
    /// <summary>
    /// Finds the optimal assignment that maximizes total score.
    /// Returns a list of (row, col, score) assignments.
    /// </summary>
    /// <param name="scores">Score matrix where rows are dropped items and columns are added items.
    /// Values should be non-negative; higher is better.</param>
    /// <param name="threshold">Minimum score for an assignment to be accepted.</param>
    public static List<(int Row, int Col, double Score)> Solve(double[,] scores, double threshold)
    {
        var rows = scores.GetLength(0);
        var cols = scores.GetLength(1);

        if (rows == 0 || cols == 0)
            return new List<(int, int, double)>();

        // Pad to square matrix
        var n = Math.Max(rows, cols);
        var cost = new double[n, n];

        // Find max score to convert maximization to minimization
        var maxScore = 0.0;
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                if (scores[i, j] > maxScore)
                    maxScore = scores[i, j];
            }
        }

        // Convert to minimization: cost = maxScore - score
        // Padding cells get cost = maxScore (score = 0)
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if (i < rows && j < cols)
                    cost[i, j] = maxScore - scores[i, j];
                else
                    cost[i, j] = maxScore;
            }
        }

        var assignment = SolveMinCost(cost, n);

        var results = new List<(int, int, double)>();
        for (var i = 0; i < rows; i++)
        {
            var j = assignment[i];
            if (j < cols)
            {
                var score = scores[i, j];
                if (score >= threshold)
                    results.Add((i, j, score));
            }
        }

        return results;
    }

    /// <summary>
    /// Standard Hungarian algorithm for minimum-cost assignment on an n×n matrix.
    /// Returns assignment[row] = col.
    /// </summary>
    private static int[] SolveMinCost(double[,] cost, int n)
    {
        // u[i] and v[j] are potentials for rows and columns
        var u = new double[n + 1];
        var v = new double[n + 1];
        // p[j] = row assigned to column j (1-indexed internally)
        var p = new int[n + 1];
        // way[j] = column of the previous node in the augmenting path
        var way = new int[n + 1];

        for (var i = 1; i <= n; i++)
        {
            // Start augmenting path from row i
            p[0] = i;
            var j0 = 0; // virtual column
            var minv = new double[n + 1];
            var used = new bool[n + 1];

            for (var j = 0; j <= n; j++)
            {
                minv[j] = double.PositiveInfinity;
                used[j] = false;
            }

            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = double.PositiveInfinity;
                var j1 = -1;

                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;

                    var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j])
                    {
                        minv[j] = cur;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }

                for (var j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            } while (p[j0] != 0);

            // Trace back the augmenting path
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        // Convert to 0-indexed assignment: assignment[row] = col
        var assignment = new int[n];
        for (var j = 1; j <= n; j++)
        {
            if (p[j] != 0)
                assignment[p[j] - 1] = j - 1;
        }

        return assignment;
    }
}
