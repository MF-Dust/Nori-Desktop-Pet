using Xunit;

// Desktop 宿主测试会共享进程级平台语义、临时配置/目录和自动化生命周期。
// 并行运行会放大 GitHub runner 调度抖动，使短时序断言偶发超时；保持同一程序集串行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
