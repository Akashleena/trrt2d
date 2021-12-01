using System.Diagnostics;
using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;

// author: Steven Clark
// steven.p.clark@gmail.com
// 2012/05/17

public class TerrainScript3d : MonoBehaviour {
	class Node {
		public Vector3 pos;
		public Vector3 parentPos;
		public int parentInd;
		public GameObject line = (GameObject) Instantiate(linePrefab) ;
		//public float heightOffset = 0.1f; //lift up a little to prevent clipping
		public float heightOffset = 0.1f;
		
		//public Node(){}
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
	private GameObject start;
	private GameObject goal;
	private bool solving = false;
	private int solvingSpeed = 10; //number of attempts to make per frame
	private float l1, m1, n1 = 0.1f;
	private float tx = 0f, tz = 0f, ty=0f; //target location to expand towards
	private bool needNewTarget = true; //keep track of whether our random sample is expired or still valid
	private int closestInd = 0;
	private int goalInd = 0; //when success is achieved, remember which node is close to goal
	//private float extendAngle = 0f;
	
	private float temperature = 1e-6f;
	private const float temperatureAdjustFactor = 2.0f;
	private const float MIN_TEMPERATURE = 1e-15f;
	private int numTransitionFails = 0;
	private const int MAX_TRANSITION_FAILS = 20;
	
	private float pGoToGoal = 0.1f;   //probability of going to goal
	private const int MAX_NUM_NODES = 10000;
	
	private float pathCost;
	public static Object linePrefab;
	
	
	public static Object pathPrefab;
	
	// Use this for initialization
	void Start () {
		coordText = GameObject.Find("Coordinate Text").GetComponent<Text>();
		statusText = GameObject.Find("Status Text").GetComponent<Text>();
		start = GameObject.Find("Start");
		goal = GameObject.Find("Goal");
		
		linePrefab = Resources.Load("LinePrefab");
		pathPrefab = Resources.Load("PathPrefab");
		
		terrainSize = Terrain.activeTerrain.terrainData.size;
		statusText.text ="terrainSize"+terrainSize;
		minX = -terrainSize.x/2;
		maxX = terrainSize.x/2;
		minZ = -terrainSize.z/2;
		maxZ = terrainSize.z/2;
        
		minHeight = 0;
		maxHeight = Mathf.Abs(terrainSize.y);

		stepSize = Mathf.Min(terrainSize.x, terrainSize.z) / 100; //TODO experiment
		statusText.text ="stepSize"+stepSize;
		//Debug.Log(minX + " " + maxX + " " + minZ + " " + maxZ + " " + minHeight + " " + maxHeight + " " + stepSize);
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
				statusText.text = "hitInfo.POint "+ hitInfo.point;
				statusText.text = "you pressed 1 "+start.transform.position;
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
			n.ConvertToPath();
			
			pathCost += GetSegmentCost(n.parentPos, n.pos);
			
			i = n.parentInd;
		}
		
		statusText.text = "Solved! with " + nodes.Count + " nodes, cost=" + pathCost;
	}
	
	float GetSegmentCost(Vector3 posA, Vector3 posB) {
		float dCost = posB.y - posA.y;
		float dx = posB.x - posA.x;
		float dz = posB.z - posA.z;
		float dy = posB.y - posA.y;
		float dist = Mathf.Sqrt(dx*dx + dz*dz + dy*dy);
		
		float cost = 0.001f*dist; //arbitrary, small distance component
		
		if(dCost > 0) {
			cost += dist*dCost;
		}
		
		return cost;
	}
	
	void TRRTGrow () {
		int numAttempts = 0;
		
		float dx, dz, dy;
		//float l, m, n1 = 0.0f;
		Vector3 pos;
		//Node n;
		
		float minDistSq;
		float distSq;
		float distdc;

		bool reachedGoal;
		
		
		while(numAttempts < solvingSpeed && nodes.Count < MAX_NUM_NODES) {
			if(needNewTarget) {
				//this condition will come true when you are very close to the goal
				if(Random.value < pGoToGoal) { //returns a random value between 0 and 1
					reachedGoal = true;
					tx = goal.transform.position.x;
					ty = goal.transform.position.y;
					tz = goal.transform.position.z;
					//Debug.Log("goal.transform.position.x " + tx);
				} else {
					// this condition will come true most of the time it gives the temporary x and z co-ordinate of the next sampled node
					reachedGoal = false;
					tx = Random.Range(minX, maxX);
					
					tz = Random.Range(minZ, maxZ);
                    ty = Random.Range(minHeight, maxHeight);
					//Debug.Log("random range of y " + ty);
					statusText.text ="ty"+ty; //331
				}
				needNewTarget = false;
				
				//Debug.Log("New target: " + tx + ", " + tz);
				
				//Find which node is closest to (tx,tz)
				minDistSq = float.MaxValue; //very big value
				for(int i=0; i<nodes.Count; i++) {
					
					dx = tx - nodes[i].pos.x; //(sampled node-current node)
					dz = tz - nodes[i].pos.z;
					dy = ty - nodes[i].pos.y;
					distSq = dx*dx + dz*dz + dy*dy ;
					Debug.Log("nodes[i].pos.y" + nodes[i].pos.y);
					if(distSq < minDistSq) {
						closestInd = i; // finding the index of nearest node in the tree measured from random sampled node tx
						minDistSq = distSq;
					}
				} 
				
				if(Mathf.Sqrt(minDistSq) <= stepSize) {
					//random sample is already "close enough" to tree to be considered reached
					if(reachedGoal) {
						FoundGoal();
						break;
					} else {
						needNewTarget = true;
						continue;
					}
				}
				
				//Debug.Log("closestInd: " + closestInd);
				
				dx = tx - nodes[closestInd].pos.x;
				dz = tz - nodes[closestInd].pos.z;
                dy = ty - nodes[closestInd].pos.y;
				//dy = ty - nodes[closestInd].pos.y;
                distdc = Mathf.Sqrt(dx*dx+dy*dy+dz*dz);
                l1 = dx/distdc;
                m1 = dy/distdc;
                n1 = dz/distdc;

				//extendAngle = Mathf.Atan2(dz, dx);
				
				//Debug.Log("dx dz a: " + dx + " " + dz + " " + extendAngle*180/Mathf.PI);
			}
			//calculate the co-ordinates of new node
			//  pos = new Vector3(nodes[closestInd].pos.x + stepSize*l1, nodes[closestInd].pos.y + stepSize*n1, nodes[closestInd].pos.z + stepSize*m1);
			 pos = new Vector3(nodes[closestInd].pos.x + stepSize*l1, nodes[closestInd].pos.y + stepSize*n1, nodes[closestInd].pos.z + stepSize*m1);
			  //get y value from terrain
			
			if(TransitionTest(nodes[closestInd].pos, pos)) {
			
				Node n = new Node(pos, nodes[closestInd].pos, closestInd, gameObject);
				nodes.Add(n);
				
				//Debug.Log("Added node " + nodes.Count + ": " + n.pos.x + ", " + n.pos.y + ", " + n.pos.z);
				
				//Determine whether we are close enough to goal
				dx = goal.transform.position.x - n.pos.x;
                dy = goal.transform.position.y - n.pos.y;
				dz = goal.transform.position.z - n.pos.z;
				if(Math.Sqrt(dx*dx + dz*dz+ dy*dy) <= stepSize) {
					//Reached the goal!
					FoundGoal();
					return;
				}
				
				//Determine whether we are close enough to target, or need to keep extending
				dx = tx - n.pos.x;
				dz = tz - n.pos.z;
                dy = ty - n.pos.y;
				if(Mathf.Sqrt(dx*dx + dz*dz + dy*dy) <= stepSize) {
					//we've reached our target point, need a new target
					needNewTarget = true;
				} else {
					//keep extending from the latest node
					closestInd = nodes.Count - 1;
				}
				numAttempts++;
			} else {
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
		
		if(slope <= 0) {
			pTransition = 1.0f; //always go "downhill"
		} else {
			pTransition = Mathf.Exp(-slope/(temperature)); //FIXME
		}
		
		bool pass = Random.value < pTransition;
		
		if(!pass) {
			if(numTransitionFails > MAX_TRANSITION_FAILS) {
				//Heat the temperature up
				temperature = temperature * temperatureAdjustFactor;
				numTransitionFails = 0; //restart counter
			} else {
				numTransitionFails++;
			}
		} else {
			//Cool the temperature down
			if(temperature > MIN_TEMPERATURE) { //prevent slim chance of temp becoming 0
				temperature = temperature / temperatureAdjustFactor;
			}
			numTransitionFails = 0;	
		}
		
		return pass;
	}
}
