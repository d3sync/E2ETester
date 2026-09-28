using System.Text;
using E2ETester.Application;
using E2ETester.Core;

namespace E2ETester.Desktop;
public sealed partial class MainViewModel
{
    public AsyncCommand ReviewTesterSetup { get; private set; }=null!;
    internal Task<IdentitySession> ResolveSetupIdentity(string alias) => (Identities.SingleOrDefault(i=>i.Alias==alias)
        ?? throw new InvalidOperationException("Sign in as source identity '"+alias+"' before reviewing tester setup.")).ResolveAsync(default);
    internal Task<PreparationPlan> ResolvePreparation(PreparationPlan plan)=>IdentityBindings.ResolveAsync(plan,ResolveSetupIdentity);
    internal async Task<string> TesterSetupPreviewAsync()
    {
        var text=new StringBuilder("Read-only tester setup review\n\nJWT claims are supplied values, not independently verified identity. No permission changes or API calls are made by this review. Database preparations resolve exactly one row.\n\n");
        var targets=new HashSet<string>();
        foreach(var test in CurrentPackage().Cases.Concat(package.GuidedActions??[]))
        {
            text.AppendLine(test.Title);
            var warnings=await IdentityBindings.ApplicabilityAsync(test,ResolveSetupIdentity);
            foreach(var warning in warnings)text.AppendLine("CHECK APPLICABILITY: "+warning);
            if(test.AppliesWhen?.Count>0&&warnings.Count==0)text.AppendLine("Group/client assumptions match the supplied claims.");
            if(test.Preparation is {} original)
            {
                try
                {
                    var plan=await ResolvePreparation(original);
                    if(IdentityBindings.KeyBinding(original) is null)text.AppendLine("Static target: verify this key belongs to the tester. It is not linked to the current login.");
                    if(plan.Kind!=PreparationKind.DatabaseValue){text.AppendLine("API preparation: review capture and restoration in Prepare.");continue;}
                    var targetLabel=$"{plan.ConnectionRef}: {plan.Table}.{plan.KeyColumn} = {plan.KeyJson}; column {plan.ValueColumn}";
                    text.AppendLine(targetLabel);
                    if(targets.Add(targetLabel))
                    {
                        var pending=preparations.LastOrDefault(p=>p.Status!="restored"&&p.Plan.ConnectionRef==plan.ConnectionRef&&p.Plan.Table==plan.Table&&p.Plan.KeyColumn==plan.KeyColumn&&p.Plan.KeyJson==plan.KeyJson&&p.Plan.ValueColumn==plan.ValueColumn);
                        var value=pending?.OriginalJson ?? await Target(new PreparationRecovery { Plan=plan,BaseUrl=BaseUrl }).ReadAsync(plan,default);
                        text.AppendLine("Original value for restoration: "+value+(pending is null?" (read now; captured and verified again before Apply)":" (from pending recovery)"));
                    }
                }
                catch(Exception ex){text.AppendLine("SETUP NEEDED: "+ex.Message);}
            }
            text.AppendLine();
        }
        text.AppendLine("Use Prepare to review and apply a role change. Use Restore changes (also available inside Guided Actions) to restore the captured original. Never substitute a fixed restore value from someone else's package. Applicability warnings can be overridden with Run anyway; assertions remain unchanged.");
        return text.ToString();
    }
    private async Task ReviewTesterSetupAsync()
    {
        Busy=true;
        try { var preview=await TesterSetupPreviewAsync();new TextDocumentWindow("Review tester setup",("Account, original values and applicability",preview)) { Owner=PreparationOwner() }.ShowDialog(); }
        finally { Busy=false; }
    }
}
