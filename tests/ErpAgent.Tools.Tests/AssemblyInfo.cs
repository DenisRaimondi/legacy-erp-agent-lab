using Xunit;

// One database, shared by every test. The cancellation tests change it and put
// it back; run in parallel with the availability tests, that restoration lands
// in the middle of somebody else's assertion — BRK-204 is 35 available until
// order 1058 is cancelled, then 45, and both are correct depending on when you
// look.
//
// Parallelism buys little here: the whole suite runs in about two seconds
// against a container on the same machine.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
