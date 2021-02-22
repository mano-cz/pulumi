using System.Threading.Tasks;
using Pulumi;

namespace aks_nextgen
{
    class Program
    {
        static Task<int> Main() => Deployment.RunAsync<MyStack>();
    }
}
