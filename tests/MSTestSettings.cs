// Test classes share nothing, so they run side by side. Methods within a class run one at a time,
// which keeps each class's shared fixture (such as the Friends server) simple.
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]
