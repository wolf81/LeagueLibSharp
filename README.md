LeagueLibSharp
==============

**PLEASE NOTE**: [SINCE RIOT HAS RELEASED AN OFFICIAL API][3], THIS PROJECT IS PUT ON HOLD.

A C# port of the Java project [LoLRTMPSClient][0] with some additional functionality. 

The eventual goal is to create a library that could be used to power a website with similar functionality as [LolKing][1] & [LolNexus][2]. A lesser priority would be to get the library running on iOS / Android using Xamarin.

Be warned: the code is still very much a work-in-progress and contains much debugging shizzle. The idea is to get things working first and then clean up the code. Also: I haven't got much experience with low-level programming, so I'll leave the bit-shift / bit-masking code pretty much as-is for now, but hopefully I can improve upon it at a later time.

[0]: http://code.google.com/p/lolrtmpsclient/
[1]: http://www.lolking.net
[2]: http://www.lolnexus.com
[3]: http://developer.riotgames.com/docs/getting-started

TODO
----

* Add convenience methods for several API calls, preferably using async / await.
* Use a ThreadPool for the all threads (RTMPS client classes).
* Implement heartbeat thread.
* Implement methods to automatically reconnect whenever connection is broken.
* Create an app project for test purposes, library project for real-world use.
* Change the LoLRTMPSClient / RTMPSClient into a state machine, seems the most logical pattern for this class.
