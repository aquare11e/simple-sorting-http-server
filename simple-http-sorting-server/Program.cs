using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web;

using System.Text;
using System.Text.RegularExpressions;

using System.Collections.Concurrent;
using System.Collections.Generic;

using System.Threading;

using System.IO;

namespace simplesortinghttpserver
{
	//runner for server and worker classes
	class Runner
	{
		public static void Main (string[] args)
		{
			Worker testWorker = new Worker ();
			HttpServer testServer = new HttpServer(8800, testWorker);

			Thread workerThread = new Thread (testWorker.Start);
			Thread serverThread = new Thread (testServer.Start);

			workerThread.Start ();
			serverThread.Start ();
		}
	}

	//simple server for handling requests
	class HttpServer 
	{
		//consts for valid request parameters
		private const String ADD_SORT_REQUEST_P1 = "concurrency";
		private const String ADD_SORT_REQUEST_P2 = "sort";
		private const String GET_SORT_RESULT = "get";

		//current listener and its port
		private TcpListener listener;

		//worker for server
		private Worker currentWorker;

		private bool isWorking;

		public HttpServer(int _port, Worker _currentWorker) 
		{
			listener = new TcpListener (IPAddress.Any, _port);
			currentWorker = _currentWorker;
			isWorking = false;
		}

		//Start server
		public void Start()
		{
			//start the listener
			listener.Start ();
			isWorking = true;

			while (isWorking) 
			{
				var client = listener.AcceptTcpClient ();
				Thread clientThread = new Thread (HandleClientRequest);
				clientThread.Start (client);
			}
		}

		//Request handling in a separate thread
		private void HandleClientRequest (object _client)
		{
			byte[] response = new byte[0];

			//safe cast client object
			TcpClient client = _client as TcpClient;

			//get the parameters
			var parameters = GetRequestParameters (client);
			if (parameters.Count == 0) {
				String responseText = "No parameters in request.";
				String responseString = "HTTP/1.1 200 OK\nContent-type: text/html\nContent-Length:"
					+ responseText.Length + "\n\n" + responseText;
				response = Encoding.ASCII.GetBytes (responseString);
			}

			//select request type
			//Check for add new sorting request
			int tryParseInt = -1;
			if (parameters.ContainsKey (ADD_SORT_REQUEST_P1)
				&& Int32.TryParse (parameters [ADD_SORT_REQUEST_P1], out tryParseInt)
				&& parameters.ContainsKey (ADD_SORT_REQUEST_P2)) {
				String jobId = AddSortRequest (tryParseInt,	parameters [ADD_SORT_REQUEST_P2]);
				String responseString = "HTTP/1.1 200 OK\nContent-type: text/html\nContent-Length:"
					+ jobId.Length.ToString () + "\n\n" + jobId;
				response = Encoding.ASCII.GetBytes (responseString);
			} 
			//Check for get sorting result request
			else if (parameters.ContainsKey (GET_SORT_RESULT)
				&& parameters[GET_SORT_RESULT].Length != 0) {
				var status = currentWorker.CheckRequestStatus (parameters[GET_SORT_RESULT]);

				if (status == JsonBuilder.state.ready) {
					response = File.ReadAllBytes (parameters[GET_SORT_RESULT] + ".json");
				} else {
					response = Encoding.ASCII.GetBytes (JsonBuilder.Build(status));
				}
			} 
			//Unknown request type
			else {
				String responseText = "Wrong parameters in request.";
				String responseString = "HTTP/1.1 200 OK\nContent-type: text/html\nContent-Length:"
					+ responseText.Length + "\n\n" + responseText;
				response = Encoding.ASCII.GetBytes (responseString);
			}

			//and send response bytes to client
			client.GetStream ().Write (response, 0, response.Length);

			//finally, close the connection
			client.Close ();
		}

		//Get parameters from client request
		private Dictionary<string, string> GetRequestParameters (TcpClient client) 
		{			
			string request = "";
			byte[] buffer = new byte[512];
			int count;

			//read bytes from client
			while ((count = client.GetStream().Read(buffer, 0, buffer.Length)) > 0)
			{
				request += Encoding.ASCII.GetString(buffer, 0, count);

				// request ends with sequence \r\n\r\n
				// or get bytes while length < 4Kb
				if (request.IndexOf("\r\n\r\n") >= 0 || request.Length > 4096)
				{
					break;
				}
			}

			//Parse parameters
			Dictionary<string, string> parameters = new Dictionary<string, string> ();

			//cut the string, take only parameters
			var matches = new Regex("(?<=GET)(.*)(?=HTTP)").Matches (request);

			//split the string, remove bad symbols and put KEY-VALUE to dictionary
			if (matches.Count != 0) {					
				String parameterString = matches [0].Value.Trim();

				foreach(string item in parameterString.Split('&')) {
					string[] parts = item.
						Replace("?", String.Empty).
						Split('=');

					if (parts.Length == 2) {
						parameters.Add (parts [0].Replace("/", String.Empty).ToLower(), parts [1]);
					}
				}
			}

			return parameters;
		}

		//Add new sort request and return "jobid"
		private String AddSortRequest(int concurrency, String arrayUrl) 
		{
			return currentWorker.AddSortRequest (concurrency, arrayUrl);
		}

	}

	//worker for sorting numbers
	class Worker
	{
		private const String ARRAY_FILE_FORMAT = ".txt";
		private const int MIN_ARRAY_SIZE_FOR_DIVIDE = 20000;
		private const int WHILE_WAITING_SLEEP_TIME = 200;

		private bool isWorking;
		private bool hasRunningSort;
		private String currentJobId;

		//use the thread-safe queue
		private ConcurrentQueue<NumThreadsUrl> requestQueue;

		public Worker()
		{
			hasRunningSort = false;
			isWorking = false;
			requestQueue = new ConcurrentQueue<NumThreadsUrl>();
		}

		public void Start ()
		{
			//set start parameters
			hasRunningSort = false;
			isWorking = true;

			while (isWorking) {
				if (!requestQueue.IsEmpty) {
					if (!hasRunningSort) {		
						hasRunningSort = true;

						//get the request from queue
						NumThreadsUrl request;
						if (!requestQueue.TryDequeue (out request)) {
							//unreachable code section
						}

						currentJobId = request.JobId;

						//next, we need to load array of numbers
						using (WebClient wc = new WebClient ()) {
							try {
								wc.DownloadFile (request.Url, request.JobId + ARRAY_FILE_FORMAT);
							}
							catch (Exception) {
								//file downlading error
								hasRunningSort = false;
								currentJobId = null;
								continue;
							}
						}

						//read file to array
						var array = ReadFileToList(request.JobId + ARRAY_FILE_FORMAT);

						//sort this array
						array = SortArray(array, request.NumberOfThreads);

						//create json file and save it to disk
						/*WriteSortedArrayToFile(JsonBuilder.Build(JsonBuilder.state.ready, array),
							request.JobId + ".json");*/
						JsonBuilder.CreateJsonResultFile (array, request.JobId + ".json");

						//clean the memory
						//clean the refferenceses from array
						array.Clear();
						//delete the array file
						File.Delete (request.JobId + ARRAY_FILE_FORMAT);
						//set null to request obj
						request = null;
						//and collect some garbage
						GC.WaitForPendingFinalizers ();
						GC.Collect ();

						hasRunningSort = false;
						currentJobId = null;
					}
				}
				Thread.Sleep (WHILE_WAITING_SLEEP_TIME);
			}
		}

		//check request status using jobid
		public JsonBuilder.state CheckRequestStatus(String jobId) {
			if (currentJobId != null && currentJobId.Equals (jobId))
				return JsonBuilder.state.progress;

			if (requestQueue.Contains (new NumThreadsUrl () { JobId = jobId }, 
				new NumThreadsUrl.ByJobIdComparer ()))
				return JsonBuilder.state.queued;

			if (File.Exists(jobId + ".json"))
				return JsonBuilder.state.ready;

			return JsonBuilder.state.eexists;
		}

		private List<int> SortArray (List<int> array, int NumberOfThreads)
		{
			//number of array elements
			int arrayElementsN = array.Count;

			//portion of elements for one thread
			int portion = arrayElementsN / NumberOfThreads;

			//subarrays for threads
			List<List<int>> subArrays = new List<List<int>> ();

			//check the array size
			//if we have enough elements,
			//then make sort on some threads
			//else make sort in this thread
			if (NumberOfThreads != 1 && arrayElementsN > MIN_ARRAY_SIZE_FOR_DIVIDE) {
				//check the portion for one thread
				if (portion < MIN_ARRAY_SIZE_FOR_DIVIDE) {
					//if portion is too small
					//then change number of sorting threads
					NumberOfThreads = arrayElementsN / MIN_ARRAY_SIZE_FOR_DIVIDE + 1;
					//calculate new portion
					portion = array.Count / NumberOfThreads;
				}

				//make array decomposition by number of threads
				for (int i = 0; i < NumberOfThreads - 1; i++) {
					subArrays.Add(array.GetRange (portion * i, portion));
				}
				subArrays.Add (array.GetRange (portion * (NumberOfThreads - 1), array.Count - portion * (NumberOfThreads - 1)));

				//sort subarrays
				Thread[] sortingThreads = new Thread[NumberOfThreads];
				for (int i = 0; i < NumberOfThreads; i++) {
					sortingThreads [i] = new Thread (subArrays[i].Sort);
					sortingThreads [i].Start ();
				}
				//wait for end of all sorts
				foreach (var thread in sortingThreads) {
					thread.Join ();
				}

				//clear the result array
				//and merge subarrays
				array.Clear ();

				//create array for tops of subarrays
				List<int> subArraysTopElement = Enumerable.Repeat (0, subArrays.Count).ToList();
				int topMinimalValueIndex;
				for (int i = 0; i < arrayElementsN; i++) {
					topMinimalValueIndex = 0;

					//find the minimal element from subarray's tops
					for (int j = 1; j < subArrays.Count; j++) {
						if (subArrays [j] [subArraysTopElement [j]] 
							< subArrays [topMinimalValueIndex] [subArraysTopElement [topMinimalValueIndex]]) {
							topMinimalValueIndex = j;
						}
					}

					//add current minimal element to the result array
					array.Add(subArrays [topMinimalValueIndex] [subArraysTopElement [topMinimalValueIndex]]);

					//check the subarray for ending
					if (subArrays [topMinimalValueIndex].Count == ++subArraysTopElement[topMinimalValueIndex]) {
						subArrays.RemoveAt (topMinimalValueIndex);
						subArraysTopElement.RemoveAt (topMinimalValueIndex);
					}
				}
			} 
			//if we don't have enough elements for thread decomposition
			//then we'll sort in current thread
			//it will faster than decomposition+sort+merge
			else {
				array.Sort ();
			}

			return array;
		}

		private List<int> ReadFileToList (String filePath) 
		{
			String stringArray;
			List<int> numberList = new List<int> ();

			//open the array file
			using (FileStream fileArrayStream = new FileStream (filePath, FileMode.Open)) {
				//Non-safety, but fast read method
				using (StreamReader sr = new StreamReader (fileArrayStream)) {
					stringArray = sr.ReadToEnd ();
					sr.Close ();
				}
				fileArrayStream.Close ();
			}
			int j;
			for (int i = 0; i < stringArray.Length; i++) {
				j = i;
				while (i < stringArray.Length && stringArray [i] != ',')
					i++;
				numberList.Add (Int32.Parse (stringArray.Substring (j, i - j)));				
			}
			//return stringArray.Split (',').Select(Int32.Parse).ToList();
			return numberList;
		}

		public String AddSortRequest (int concurrency, String arrayUrl)
		{
			//Generate jobid
			String jobId = GenerateJobId ();

			//create new queue element and adds into the queue
			NumThreadsUrl queueItem = new NumThreadsUrl () 
			{ JobId = jobId, NumberOfThreads = concurrency, Url = arrayUrl };
			requestQueue.Enqueue (queueItem);

			//return jobid to server
			return jobId;
		}

		//generates semi-unique id for server jobs
		private String GenerateJobId () 
		{
			return Guid.NewGuid().ToString().GetHashCode().ToString("x");
		}

		//little class for store planned jobs in queue
		private class NumThreadsUrl 
		{
			public String JobId { get; set; }
			public int NumberOfThreads { get; set; }
			public String Url { get; set; }

			public class ByJobIdComparer : IEqualityComparer<NumThreadsUrl>
			{
				public bool Equals (NumThreadsUrl first, NumThreadsUrl second)
				{
					return first.JobId.Equals (second.JobId);
				}

				public int GetHashCode(NumThreadsUrl obj)
				{
					return obj.GetHashCode ();
				}
			}
		}
	}

	//simple json-response generator
	class JsonBuilder 
	{
		public enum state {ready, queued, progress, eexists};

		public static String Build (state requestState)
		{
			StringBuilder jsonString = new StringBuilder ();

			jsonString.AppendLine ("{");
			jsonString.AppendLine (@"	""state"": """ + requestState.ToString() + "\",");
			jsonString.AppendLine (@"	""data"": []");
			jsonString.Append ("}");

			return jsonString.ToString();
		}

		public static void CreateJsonResultFile (List<int> data, String fileName)
		{
			StringBuilder buffer = new StringBuilder();
			String stringBuffer;
			using (FileStream fs = new FileStream (fileName, FileMode.Create)) {
				using (BufferedStream bs = new BufferedStream (fs)) {
					buffer.AppendLine ("{");
					buffer.AppendLine (@"	""state"": """ + state.ready.ToString() + "\",");
					buffer.Append (@"	""data"": [");
					bs.Write(Encoding.ASCII.GetBytes(buffer.ToString()), 0, buffer.Length);

					for (int i = 0; i < data.Count - 1; i++) {
						stringBuffer = data [i] + ", ";
						bs.Write (Encoding.ASCII.GetBytes (stringBuffer), 0, stringBuffer.Length);
					}
					stringBuffer = data [data.Count - 1].ToString();
					bs.Write (Encoding.ASCII.GetBytes (stringBuffer), 0, stringBuffer.Length);

					bs.Close ();
				}
				fs.Close ();
			}
		}
	}
}