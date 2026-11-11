using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using SWToolsHub.Portable;

class Scenario {
    public int Book {get;set;}
    public string Label {get;set;}
    public CalculationInput[] Inputs {get;set;}
    public CalculationInput[] Results {get;set;}
}
class Checks
{
    static object Plain(object v) {
        if (!(v is JsonElement)) return v;
        var j=(JsonElement)v;
        switch(j.ValueKind) {
            case JsonValueKind.Number:return j.GetDouble();
            case JsonValueKind.String:return j.GetString();
            case JsonValueKind.True:return true;
            case JsonValueKind.False:return false;
            default:return null;
        }
    }
    static int Main(string[] args) {
        using var stream=typeof(Checks).Assembly.GetManifestResourceStream("Workbooks.json");
        var books=JsonSerializer.Deserialize<CalculationBook[]>(stream, new JsonSerializerOptions { MaxDepth = 256 });
        foreach(var b in books) foreach(var s in b.Sheets) foreach(var c in s.Cells) {c.Value=Plain(c.Value);c.Expected=Plain(c.Expected);}
        int failed=0,total=0;
        void Check(bool condition,string label) { total++; if(!condition) {failed++;Console.WriteLine("FAIL "+label);} }
        bool Same(object actual,object expected) {
            expected=Plain(expected);
            return expected is double && actual!=null && !(actual is string) && !(actual is bool)
                ? Math.Abs(Convert.ToDouble(actual)-(double)expected)<=1e-8*Math.Max(1,Math.Abs((double)expected))
                : Equals(actual,expected) || expected==null && Equals(actual,"");
        }

        foreach(var b in books) {
            var engine=new CalculationEngine(b); int count=0;
            foreach(var s in b.Sheets) foreach(var c in s.Cells.Where(c=>c.Expression!=null)) {
                total++;count++;
                try {
                    var actual=engine.Get(s.Name,c.Address);var expected=c.Expected;
                    bool equal=expected is double && actual!=null && !(actual is string) && !(actual is bool)
                        ? Math.Abs(Convert.ToDouble(actual)-(double)expected)<=1e-8*Math.Max(1,Math.Abs((double)expected))
                        : Equals(actual,expected) || expected==null && Equals(actual,"");
                    if(!equal) { if(failed<25) Console.WriteLine($"FAIL {b.Source}/{s.Name}!{c.Address}: actual={actual} expected={expected}");failed++; }
                } catch(Exception ex) {if(failed<25) Console.WriteLine($"FAIL {b.Source}/{s.Name}!{c.Address}: {ex.Message}");failed++;}
            }
            Console.WriteLine($"{b.Source}: compared {count} formula cells");
        }
        {
            using var oracleStream=typeof(Checks).Assembly.GetManifestResourceStream("SpreadsheetScenarios.json");
            var scenarios=args.Length>0 ? JsonSerializer.Deserialize<Scenario[]>(File.ReadAllText(args[0])) : JsonSerializer.Deserialize<Scenario[]>(oracleStream);
            Check(scenarios.Length == 256, "All 256 independent scenarios present");
            foreach(var scenario in scenarios) {
                var engine=new CalculationEngine(books[scenario.Book]);
                foreach(var input in scenario.Inputs) engine.SetInput(input.Sheet,input.Address,Plain(input.Value));
                foreach(var result in scenario.Results) {
                    try {Check(Same(engine.Get(result.Sheet,result.Address),result.Value),scenario.Label+"/"+result.Sheet+"!"+result.Address+" actual="+engine.Get(result.Sheet,result.Address)+" expected="+Plain(result.Value));}
                    catch(Exception ex) {Check(false,scenario.Label+" "+ex.Message);}
                }
            }
            Console.WriteLine("Independent spreadsheet scenarios executed: "+scenarios.Length);
        }
        // Mutation, save/load, rollback and validation exercise behavior beyond cached fixtures.
        var height=new CalculationEngine(books[4]);
        height.SetInput("Высота узла","B3","3000,5");
        Check(Same(height.Get("Высота узла","B11"),2496.5),"Russian decimal input and invalidation");
        var saved=height.SaveSession();height.Reset();
        Check(Same(height.Get("Высота узла","B11"),1646d),"Reset original inputs");
        var sessionJson=JsonSerializer.Serialize(saved);
        saved=JsonSerializer.Deserialize<CalculationSession>(sessionJson);
        foreach(var input in saved.Inputs)input.Value=Plain(input.Value);
        height.LoadSession(saved);Check(Same(height.Get("Высота узла","B11"),2496.5),"Session roundtrip");
        saved.Inputs=saved.Inputs.Concat(new[]{new CalculationInput{Sheet="Высота узла",Address="B11",Value=999d}}).ToArray();
        try {height.LoadSession(saved);Check(false,"Formula overwrite rejected");} catch(CalculationError){Check(Same(height.Get("Высота узла","B11"),2496.5),"Failed import leaves inputs intact");}
        foreach(var bad in new[]{"", "abc", "NaN", "Infinity", "1e999"}) {
            try {height.SetInput("Высота узла","B3",bad);Check(false,"Reject invalid "+bad);}catch(CalculationError){Check(true,"Rejected invalid input");}
        }
        var beam=new CalculationEngine(books[2]);beam.SetInput("Расчёт","B8",801d);
        Check(beam.ValidateInputs("Расчёт")!=null,"Load outside span rejected");
        beam.SetInput("Расчёт","B8",400d);beam.SetInput("Расчёт","B11",0d);
        Check(beam.ValidateInputs("Расчёт")!=null,"Zero safety factor rejected");
        var bolts=new CalculationEngine(books[3]);bolts.SetInput("Болты","C8",0d);
        Check(bolts.ValidateInputs("Болты")!=null,"Zero bolt count rejected");
        bolts.SetInput("Болты","C8",4.5);Check(bolts.ValidateInputs("Болты")!=null,"Fractional bolt count rejected");
        bolts.SetInput("Болты","C8",4d);bolts.SetInput("Болты","C44",50d);
        Check(bolts.ValidateInputs("Болты")!=null,"Nonpositive net gusset section rejected");
        var pipe=new CalculationEngine(books[0]);pipe.SetInput("Местное смятие","B6",0d);
        Check(pipe.ValidateInputs("Местное смятие")!=null,"Zero pipe wall cannot report pass");
        bolts.Reset();bolts.SetInput("Болты","C12","С590");
        Check(bolts.ValidateInputs("Болты")!=null,"Missing source bearing resistance is explicit");
        var otherVersion=height.SaveSession();otherVersion.Sha256="different";
        try {height.LoadSession(otherVersion);Check(false,"Reject incompatible formula version");}
        catch(CalculationError){Check(Same(height.Get("Высота узла","B11"),2496.5),"Version mismatch preserves session");}
        foreach(var book in books) {
            var engine=new CalculationEngine(book);
            foreach(var sheet in book.Sheets.Where(s=>!s.ReferenceOnly))Check(engine.ValidateInputs(sheet.Name)==null,"Original inputs valid: "+sheet.Name);
        }
        Console.WriteLine($"Calculation checks: {total-failed}/{total} passed; {failed} failed");
        return failed==0?0:1;
    }
}
