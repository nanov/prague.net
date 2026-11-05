Rules for this run:
- Work ONLY inside your workspace directory (given below). Create source files there; `Service.csproj` already references the Prague NuGet package (__VERSION__) plus NUnit and Microsoft.Extensions.Hosting. Do not edit package versions.
- Documentation you MAY use (this is what any user of the library has):
  - the package XML docs and DLLs under ~/.nuget/packages/prague*/ (you may inspect/decompile them);
  - the library's LLM docs, copied locally: __DOCS__/llms.txt (index of docs pages) and __DOCS__/llms-full.txt (dense reference);
  - the public docs site https://nanov.github.io/prague.net/ (any page under it, via web fetch). No other web access.
- Do NOT read anything under __REPO_PARENT__/ (the library's source repo), and do not read any other directory under __EVAL_ROOT__ or its parent scratchpad except your workspace and __DOCS__ and __EVAL_ROOT__/tasks.
- __SKILLS_RULE__
- Finish with `dotnet build` and `dotnet test` passing in your workspace (warnings are fine). If you cannot get there, leave your best attempt and say what fails.
- Final reply: a list of files you wrote and 3-6 bullets on your design decisions. Keep it short.
