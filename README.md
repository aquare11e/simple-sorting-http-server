# Simple sorting HTTP server

This project is small console application, built in C#. It is multi-thread HTTP/1.1 server that can accept user requests to sort arrays of integer. It used TcpListener (from System.Net.Sockets) for listen the clients' requests and TcpClient (from System.Net.Sockets) to respond.

## Build

For build this project on Linux you need download it (e.g. using curl or wget), unzip and compile Program.cs file using [Mono compiler](http://www.mono-project.com/docs/about-mono/languages/csharp/).

```bash
#compile
mcs Program.cs

#run background process
exec mono Program.exe &
```
On Windows download and compile project using Mono or Visual Studio.

## Use

For use server, open the Web-browser and tape:
```
http://<YOUR-IP>:8800/?concurrency=<NUMBER_OF_THREADS>&sort=<ARRAY_OF_INT_URL>
```
Command for send request to sort array. It returns the Job ID or error description.

```
http://<YOUR-IP>:8800/?get=<JobID>
```
Command for get sorting result. It returns JSON object with request status (field "state") and sorted array (field "data"), if sort is finished.
Field "state" can be: "ready" - sort is finished, "queued" - request in the queue, "progress" - request in the sorting process.

Where:
YOUR-IP - IP computer with a running process (if process runned on current machine use "localhost" or "127.0.0.1");
NUMBER_OF_THREADS - number of threads for sorting algorithm;
ARRAY_OF_INT_URL - resource URL with array of integer for sorting;
JobID - Sorting job ID 

