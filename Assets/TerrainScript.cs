// using System.Reflection.PortableExecutable;
using System.Numerics;
using System.Diagnostics;
using UnityEngine;
//using System;
using System.Collections.Generic;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;
using Object= UnityEngine.Object;
using Random=UnityEngine.Random;



public class TerrainScript : MonoBehaviour {
	class Node {
		public Vector3 pos;
		public Vector3 parentPos;
		public int parentInd;
		public GameObject line = null;
		public float heightOffset = 0.5f; //lift up a little to prevent clipping
		
		public Node(Vector3 _pos, Vector3 _parentPos, int _parentInd, GameObject obj) {
			pos = _pos;
			parentPos = _parentPos;
			parentInd = _parentInd;
			
			//obj = (GameObject) Instantiate(Resources.Load("Waypoint"), pos, Quaternion.identity);
			
			if(parentInd >= 0) {
				line = (GameObject) Instantiate(linePrefab);
				LineRenderer lr = line.GetComponent<LineRenderer>();
				lr.SetPosition(0,pos + new Vector3(0f,heightOffset,0f));
				lr.SetPosition(1,parentPos + new Vector3(0f,heightOffset,0f));
			}
		}
		
		public void ConvertToPath() {
			GameObject.Destroy(line);
			line = (GameObject) Instantiate(pathPrefab);
			LineRenderer lr = line.GetComponent<LineRenderer>();
			lr.SetPosition(0,pos + new Vector3(0f,heightOffset,0f));
			lr.SetPosition(1,parentPos + new Vector3(0f,heightOffset,0f));
		}
	};
	
	
	private float stepSize;
	private Text coordText;
	private Text statusText;
	private Vector3 terrainSize; //remember, Y is height
	private float minX, maxX, minZ, maxZ, minHeight, maxHeight;
	private List<Node> nodes = new List<Node>();
	//private Vector3 obstaclepos;
	private GameObject start;
	private GameObject goal;
	private bool solving = false;
	private int solvingSpeed = 1; //number of attempts to make per frame
	
	private float tx = 0f, tz = 0f; //target location to expand towards
	private bool needNewTarget = true; //keep track of whether our random sample is expired or still valid
	private int closestInd = 0; //the last node in the tree (the node before goal node)
	private int goalInd = 0; //when success is achieved, remember which node is close to goal
	private float extendAngle = 0f;
	
	private float temperature = 1e-6f;
	private const float temperatureAdjustFactor = 2.0f;
	private const float MIN_TEMPERATURE = 1e-15f;
	private int numTransitionFails = 0;
	private const int MAX_TRANSITION_FAILS = 20;
	
	private float pGoToGoal = 0.1f;
	private const int MAX_NUM_NODES = 10000;
    public GameObject[] gameObjects;
	
	private float pathCost=0;
	public List<Vector3> obstacleCoord;
	
	// public List<List<Vector3>> obstaclesList = new List<List<Vector3>>();
	//public List<List> obstacles
	public static Object linePrefab;
	public static Object pathPrefab;
	public Vector3 ScaleofObs;
	public int k=0;
	
	// Use this for initialization
	void Start () {

		//This goes in the canvas
		coordText = GameObject.Find("Coordinate Text").GetComponent<Text>();
		statusText = GameObject.Find("Status Text").GetComponent<Text>();
		start = GameObject.Find("Start");
		goal = GameObject.Find("Goal");
		
		linePrefab = Resources.Load("LinePrefab");
		pathPrefab = Resources.Load("PathPrefab");	
		terrainSize = Terrain.activeTerrain.terrainData.size;
		//minX = -terrainSize.x/2;
		minX = 2;
		maxX = terrainSize.x- 2;
		//minZ = -terrainSize.z/2;
		minZ = 2;
		// maxZ = terrainSize.z/2;
		maxZ = terrainSize.z- 2;
		minHeight = 0;
		maxHeight = terrainSize.y;
		stepSize = Mathf.Min(terrainSize.x, terrainSize.z) / 100; //TODO experiment
		
		// Debug.Log("minX" + minX + " " + "maxX" + maxX + " " + "minZ" + minZ + " " + "maxZ" + maxZ + " " + minHeight + " " + maxHeight + " " + "terrainSize" + terrainSize);
		Vector3 cubeposcoord0, cubeposcoord1, cubeposcoord2, cubeposcoord3;
		
		GameObject[] gameObjects  = GameObject.FindGameObjectsWithTag("Cube");
		foreach (GameObject go in gameObjects)
        {
            Vector3 cubecentre = go.transform.position;
			Vector3 scalecube = go.transform.localScale;
			// Debug.Log("scale" + scalecube);
			
			//getting the coordinates of the corners of the square obstacles
			cubeposcoord3.x=cubecentre.x + (scalecube.x/2);
			cubeposcoord3.y=0;
			cubeposcoord3.z=cubecentre.z - (scalecube.z/2);
			cubeposcoord0.x=cubeposcoord3.x - scalecube.x;
			cubeposcoord0.z=cubeposcoord3.z;
		    cubeposcoord0.y=cubeposcoord1.y=cubeposcoord2.y=cubeposcoord3.y;
		    cubeposcoord1.x=cubeposcoord3.x- scalecube.x;
		    cubeposcoord1.z=cubeposcoord3.z + scalecube.x;
		    cubeposcoord2.x=cubeposcoord3.x;
		    cubeposcoord2.z=cubeposcoord3.z + scalecube.z;
            // Debug.Log("cube coordinates" + cubeposcoord0 + cubeposcoord1 + cubeposcoord2 + cubeposcoord3);
			obstacleCoord.Add(cubeposcoord0);
			obstacleCoord.Add(cubeposcoord1);
			obstacleCoord.Add(cubeposcoord2);
			obstacleCoord.Add(cubeposcoord3);
			// obstaclesList.Add(obstacleCoord);
			// Debug.Log("obstacles" + obstacles[k]);
			// Debug.Log("obstacle x coord" + obstacles[0][0].x);
			k++;
			// Debug.Log(k);
		}

		// for(int i = 0; i < k; i++) {
		// 	Debug.Log("Printing " + i + " element");
		// 	int len = obstaclesList[i].Count;
		// 	for(int j = 0; j < len; j++) {
		// 		Debug.Log(obstaclesList[i][j]);
		// 	}
		// }
		
	}
	
	// Update is called once per frame
	void Update () {
		if(Input.GetKeyDown("s")) {
			BeginSolving(10);
		}
		
		if(Input.GetKeyDown("p")) {
			PauseSolving();
		}
		
		if(Input.GetKeyDown("c")) {
			ClearTree();
		}
		
		if(solving) {
			if(nodes.Count < MAX_NUM_NODES) {
				statusText.text = "Solving... (nodes="+nodes.Count+", temp=" + temperature.ToString("0.00E00") + ")";
				TRRTGrow();
			}
		}
	}
	
	void OnMouseOver () {
		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hitInfo;
		
		if(GetComponent<Collider>().Raycast(ray, out hitInfo, 5000)) { //TODO replace with camera far plane distance
			//Debug.Log(hitInfo.point);
			coordText.text = hitInfo.point.ToString();
			
			if(Input.GetKey("1")) {
				start.transform.position = hitInfo.point;
			}
			
			if(Input.GetKey("2")) {
				goal.transform.position = hitInfo.point;
			}
		}
	}
	
	void OnMouseExit () {
		coordText.text = "";
	}
	
	
	void BeginSolving(int speed) {
		solvingSpeed = speed;
		if(!solving) {
			solving = true;
			if(nodes.Count < 1) {
				//add initial node
				Node n = new Node(start.transform.position, start.transform.position, -1, gameObject);
				nodes.Add(n);
				
				//Debug.Log("Added node " + nodes.Count + ": " + n.pos.x + ", " + n.pos.y + ", " + n.pos.z);
			}
		}
	}
	
	void PauseSolving() {
		solving = false;
		statusText.text = "Solving paused.";
	}
	
	void ClearTree() {
		solving = false;
		statusText.text = "Tree cleared.";
		//FIXME
		for(int i=0; i<nodes.Count; i++) {
			GameObject.Destroy(nodes[i].line);
		}
		nodes.Clear();
		needNewTarget = true;
	}
	
	
	void FoundGoal() {
		goalInd = closestInd; 
		solving = false;
		
		
		//trace path backwards to highlight navigation path
		int i = goalInd;
		Node n;
		
		pathCost = GetSegmentCost(nodes[goalInd].pos, goal.transform.position);
		
		while(i != 0) {
			n = nodes[i];
			
			Debug.Log("node co-ordinates of the green line " + nodes[i]);
			
			
			n.ConvertToPath();
			
			pathCost += GetSegmentCost(n.parentPos, n.pos);
			
			i = n.parentInd;
		
		}
		
		statusText.text = "Solved! with " + nodes.Count + " nodes, cost=" + pathCost;
	}
	
	float GetSegmentCost(Vector3 posA, Vector3 posB) {
		//float dCost = posB.y - posA.y; // this y co-ordinate was considered for terrain height
		float dx = posB.x - posA.x;
		float dz = posB.z - posA.z;
		
		float dist = Mathf.Sqrt(dx*dx + dz*dz);
		
		float cost = 0.001f*dist; //arbitrary, small distance component
		
		//if(dCost > 0) {
			//cost += dist*dCost;
		//}
		
		return cost;
	}
	bool isNodeinsideObstacle(Vector3 pos1)
	{
		//we check whether node lies inside the obstacle
		for (int j=0; j<k; j++)
		{
			// Debug.Log("checking node co-ordinate for obstacle" + pos1.x + pos1.z);
			// Debug.Log("obstaclesList[j][0]" + obstaclesList[j][0]);
			// Debug.Log("obstaclesList[j][3]" + obstaclesList[j][3]);
			if ((pos1.x>=obstacleCoord[j * 4 + 0].x && pos1.x<=obstacleCoord[j * 4 + 2].x)&&(pos1.z>=obstacleCoord[j * 4 + 0].z && pos1.z<=obstacleCoord[j * 4 + 2].z))
			{
				Debug.Log("new Node will be generated inside obstacle so we don't add it to the nodes list");
				
				
				return true;
				// break;
			}
			
		}
		return false;
	}
	bool isLineinsideObstacle(Vector3 pos1, Vector3 parentpos1){

		float x1=parentpos1.x;
		float y1=parentpos1.y;
		float x2=pos1.x;
		float y2=pos1.y;
		float dy = y2-y1;
		float dx= x2-x1;
		float theta= Mathf.Atan2(dy, dx);
		
		
		float x=x1+(float)0.5*Mathf.Cos(theta); //create sample points on the line joining co-ordinates (x1,y1) and (x2,y2) and check if these sample points lie inside the obstacle
	    float y=y1+(float)0.5*Mathf.Sin(theta);
		//we are checking whether line joining 2 nodes lies inside obstacle
		for (int j=0; j<k; j++)
		{
			if ((pos1.x>=obstacleCoord[j * 4 + 0].x && pos1.x<=obstacleCoord[j * 4 + 2].x)&&(pos1.z>=obstacleCoord[j * 4 + 0].z && pos1.z<=obstacleCoord[j * 4 + 2].z))
			{
				Debug.Log("line or path  is Crossing obstacle");
			
				return true;
				// break;
			}
			x=x+(float)0.5*Mathf.Cos(theta);
			y=y+(float)0.5*Mathf.Sin(theta);	
		}
		
		return false;
	}
	
	void TRRTGrow () {
		int numAttempts = 0;
		
		float dx, dz;
		
		Vector3 pos;
		Node n;
		
		float minDistSq;
		float distSq;
		
		bool goingToGoal;
		// Debug.Log ("check vertices" + newPos);

		while(numAttempts < solvingSpeed && nodes.Count < MAX_NUM_NODES) 
		{
			if(needNewTarget) {
				if(Random.value < pGoToGoal) {
					goingToGoal = true;
					tx = goal.transform.position.x;
					tz = goal.transform.position.z;
				} else {
					goingToGoal = false;
					tx = Random.Range(minX, maxX);
					tz = Random.Range(minZ, maxZ);
				}
				needNewTarget = false;
				
				//Debug.Log("New target: " + tx + ", " + tz);
				
				//Find which node is closest to (tx,tz)
				minDistSq = float.MaxValue;
				for(int i=0; i<nodes.Count; i++) {
					dx = tx - nodes[i].pos.x;
					dz = tz - nodes[i].pos.z;
					distSq = dx*dx + dz*dz;
					if(distSq < minDistSq) {
						closestInd = i;
						minDistSq = distSq;
					}
				}
				
				if(Mathf.Sqrt(minDistSq) <= stepSize) {
					//random sample is already "close enough" to tree to be considered reached
					if(goingToGoal) {
						FoundGoal();
						break;
					} else {
						needNewTarget = true;
						continue;
					}
				}
				//Debug.Log("obstacles" + obstacles[0]);
				//Debug.Log("closestInd: " + closestInd);
				
				dx = tx - nodes[closestInd].pos.x;
				dz = tz - nodes[closestInd].pos.z;
				
				extendAngle = Mathf.Atan2(dz, dx);
				
				//Debug.Log("dx dz a: " + dx + " " + dz + " " + extendAngle*180/Mathf.PI);
			}
			
			pos = new Vector3(nodes[closestInd].pos.x + stepSize*Mathf.Cos(extendAngle), 0f, nodes[closestInd].pos.z + stepSize*Mathf.Sin(extendAngle));
			pos.y = Terrain.activeTerrain.SampleHeight(pos); //get y value from terrain
			
			if(TransitionTest(nodes[closestInd].pos, pos)) {
			    if (!(isNodeinsideObstacle(pos)))
				{
					if (!(isLineinsideObstacle(pos,nodes[closestInd].pos)))
					{
					n = new Node(pos, nodes[closestInd].pos, closestInd, gameObject);
					nodes.Add(n);
					Debug.Log("Added node " + nodes.Count + ": " + n.pos.x + ", " + n.pos.y + ", " + n.pos.z);

				
		
				//Determine whether we are close enough to goal
					dx = goal.transform.position.x - n.pos.x;
					dz = goal.transform.position.z - n.pos.z;
					if(Mathf.Sqrt(dx*dx + dz*dz) <= stepSize) {
					//Reached the goal!
					FoundGoal();
					return;
					}
				
				//Determine whether we are close enough to target, or need to keep extending
					dx = tx - n.pos.x;
					dz = tz - n.pos.z;
					if(Mathf.Sqrt(dx*dx + dz*dz) <= stepSize) {
					//we've reached our target point, need a new target
					needNewTarget = true;
					} 
					else 
					{
					//keep extending from the latest node
					closestInd = nodes.Count - 1;
					}
					numAttempts++;
				}
				}
			} 
			
			else {
				//this extension is aborted due to transition test, need a new target
				//Debug.Log("Failed transition test");
				needNewTarget = true;
			}
		}
	}
	
	bool TransitionTest (Vector3 posA, Vector3 posB) {
		float dx = posB.x - posA.x;
		float dz = posB.z - posA.z;
		float dist = Mathf.Sqrt(dx*dx + dz*dz);
		
		float slope = (posB.y - posA.y) / dist;
		
		float pTransition; //transition probability, 0 to 1
		
		if(slope <= 0) 
		{
			//pTransition = 1.0f; //always go "downhill"
			pTransition = 0.5f; 
		} 
		else 
		{
			pTransition = Mathf.Exp(-slope/(temperature)); //FIXME
		}
		
		bool pass = Random.value < pTransition;
		
		if(!pass) 
		{
			if(numTransitionFails > MAX_TRANSITION_FAILS) 
			{
				//Heat the temperature up
				temperature = temperature * temperatureAdjustFactor;
				numTransitionFails = 0; //restart counter
			} 
			else 
			{
				numTransitionFails++;
			}
		} 
		else 
		{
			//Cool the temperature down
			if(temperature > MIN_TEMPERATURE) 
			{ //prevent slim chance of temp becoming 0
				temperature = temperature / temperatureAdjustFactor;
			}
			numTransitionFails = 0;	
		}
		
		return pass;
	}
}
