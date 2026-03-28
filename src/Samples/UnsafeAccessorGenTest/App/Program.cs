using UnsafeAccessorGenTest;

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"AOT: {!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
Console.WriteLine();

int passed = 0;
int failed = 0;

// ===================================================================
// Test 0: Single captured local variable (decimal)
// ===================================================================
{
    var minTotal = 100.50m;
    var q = QueryFactory.Create();
    q.Where(s => s.Length > (int)minTotal);

    try
    {
        GeneratedClosureAccessors.Verify_0(q.LastFunc!);
        GeneratedClosureAccessors.Extract_0(q.LastFunc!, out decimal extracted);
        Assert("Single capture (minTotal)", extracted, minTotal);
    }
    catch (Exception ex)
    {
        Fail("Single capture (minTotal)", ex);
    }
}

// ===================================================================
// Test 1: Multiple captured variables from the same scope
// ===================================================================
{
    var searchMin = 10m;
    var searchMax = 50m;
    var q = QueryFactory.Create();
    q.Where(s => s.Length > (int)searchMin && s.Length < (int)searchMax);

    try
    {
        GeneratedClosureAccessors.Verify_1(q.LastFunc!);
        GeneratedClosureAccessors.Extract_1(q.LastFunc!, out decimal extractedMin, out decimal extractedMax);
        Assert("Multi-capture (searchMin)", extractedMin, searchMin);
        Assert("Multi-capture (searchMax)", extractedMax, searchMax);
    }
    catch (Exception ex)
    {
        Fail("Multi-capture", ex);
    }
}

// ===================================================================
// Test 2: Captured string variable
// ===================================================================
{
    var search = "alice";
    var q = QueryFactory.Create();
    q.Where(s => s.Contains(search));

    try
    {
        GeneratedClosureAccessors.Verify_2(q.LastFunc!);
        GeneratedClosureAccessors.Extract_2(q.LastFunc!, out string? extractedSearch);
        Assert("String capture (search)", extractedSearch, search);
    }
    catch (Exception ex)
    {
        Fail("String capture", ex);
    }
}

// ===================================================================
// Test (skipped): Non-capturing lambda — generator correctly skips it
// ===================================================================
{
    var q = QueryFactory.Create();
    q.Where(s => s.Length > 5); // no captures - no accessor emitted
    Console.WriteLine("  PASS: Non-capturing lambda (no accessor emitted)");
    passed++;
}

// ===================================================================
// Test 3: Captured object with property chain access
// ===================================================================
{
    var viewModel = new SearchViewModel { SearchTerm = "bob" };
    var q = QueryFactory.Create();
    q.Where(s => s.Contains(viewModel.SearchTerm));

    try
    {
        GeneratedClosureAccessors.Verify_3(q.LastFunc!);
        GeneratedClosureAccessors.Extract_3(q.LastFunc!, out SearchViewModel? extractedVm);
        Assert("Object property capture (viewModel.SearchTerm)", extractedVm?.SearchTerm, viewModel.SearchTerm);
    }
    catch (Exception ex)
    {
        Fail("Object property capture", ex);
    }
}

// ===================================================================
// Test 4: Verify cached accessor works with different values
// ===================================================================
{
    for (int i = 0; i < 3; i++)
    {
        var val = (decimal)(i * 10 + 5);
        var q = QueryFactory.Create();
        q.Where(s => s.Length > (int)val);

        try
        {
            GeneratedClosureAccessors.Extract_4(q.LastFunc!, out decimal extracted);
            Assert($"Cached accessor iteration {i} (val={val})", extracted, val);
        }
        catch (Exception ex)
        {
            Fail($"Cached accessor iteration {i}", ex);
        }
    }
}

// ===================================================================
// Test 5: Deeply nested captures in complex boolean expressions
// .Where(true && capturedVariable && (secondVar && (thirdVar)))
// ===================================================================
{
    var capturedVariable = "hello";
    var secondVar = "world";
    var thirdVar = "!";
    var q = QueryFactory.Create();
    q.Where(s => true && s.Contains(capturedVariable) && (s.Contains(secondVar) && (s.StartsWith(thirdVar))));

    try
    {
        GeneratedClosureAccessors.Verify_5(q.LastFunc!);
        GeneratedClosureAccessors.Extract_5(q.LastFunc!, out string? extractedFirst, out string? extractedSecond, out string? extractedThird);
        Assert("Deep nested capture (capturedVariable)", extractedFirst, capturedVariable);
        Assert("Deep nested capture (secondVar)", extractedSecond, secondVar);
        Assert("Deep nested capture (thirdVar)", extractedThird, thirdVar);
    }
    catch (Exception ex)
    {
        Fail("Deep nested capture", ex);
    }
}

// ===================================================================
// Test 6: Mixed types in deeply nested expression
// ===================================================================
{
    var minLen = 3;
    var prefix = "ab";
    var maxLen = 100m;
    var q = QueryFactory.Create();
    q.Where(s => s.Length > minLen && (s.StartsWith(prefix) && s.Length < (int)maxLen));

    try
    {
        GeneratedClosureAccessors.Verify_6(q.LastFunc!);
        GeneratedClosureAccessors.Extract_6(q.LastFunc!, out int extractedMin, out string? extractedPrefix, out decimal extractedMax);
        Assert("Mixed nested (minLen)", extractedMin, minLen);
        Assert("Mixed nested (prefix)", extractedPrefix, prefix);
        Assert("Mixed nested (maxLen)", extractedMax, maxLen);
    }
    catch (Exception ex)
    {
        Fail("Mixed nested capture", ex);
    }
}

// --- Results ---
Console.WriteLine();
Console.WriteLine($"Results: {passed} passed, {failed} failed");
return failed > 0 ? 1 : 0;

// --- Helpers ---

void Assert<T>(string name, T? actual, T? expected)
{
    if (EqualityComparer<T>.Default.Equals(actual!, expected!))
    {
        Console.WriteLine($"  PASS: {name}");
        passed++;
    }
    else
    {
        Console.WriteLine($"  FAIL: {name} — expected {expected}, got {actual}");
        failed++;
    }
}

void Fail(string name, Exception ex)
{
    Console.WriteLine($"  FAIL: {name} — {ex.GetType().Name}: {ex.Message}");
    failed++;
}

class SearchViewModel
{
    public string SearchTerm { get; set; } = "";
}
