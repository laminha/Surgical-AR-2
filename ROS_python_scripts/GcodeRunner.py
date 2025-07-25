import dvrk
import numpy as np
import quaternion as np_quat
import rospy
import PyKDL as kdl
import crtk
import copy
from std_msgs.msg import String
from sensor_msgs.msg import JointState

# dVRK boilerplate.
ros_abstraction_layer = crtk.ral('CompTest') 
psm = dvrk.rmis(ros_abstraction_layer,'PSM2')

# Ros topic subscribing boilerplate.
gGcodeString = ''
gGcodeChanged = False
topic_name = '/gcode'
def GcodeCallback(msg):
  global gGcodeString
  global gGcodeChanged
  gGcodeString = msg.data
  gGcodeChanged = True
rospy.Subscriber(topic_name, String, GcodeCallback);

# ROS publishing boilerplate.
gJawPublisher = rospy.Publisher('/PSM2/jaw/move_jp', JointState, queue_size=10)

# Initialize kinematic frames. (magic numbers are empirical)
initial_orientation_rot = kdl.Rotation.RotX(-0.05) * kdl.Rotation.RotZ(0.224199863) * kdl.Rotation.Quaternion(0.7071068, 0.7071068, 0.0, 0.0)
gStartFrame = kdl.Frame(initial_orientation_rot, kdl.Vector(-0.003, -0.004, -0.12))

# Move to start position.
print("sleeping")
for i in range(3000):
  psm.move_cp(gStartFrame)
  rospy.sleep(0.001)
print("program start...")

# PSM movement function with globals (used only as static vars).
# Current support is only for G0 and G1 moves.
gFeedrate = 1.0
gXPos = 0.
gYPos = 0.
gZPos = 0.
gRoll = 0.
gPitch = 0.
gYaw = 0.
gJawPos = 0.
gPrevFrame = copy.deepcopy(gStartFrame)
def ProcessInstructionStr(instruction):
  # Declare global variables.
  global gXPos # In m.
  global gYPos # In m.
  global gZPos # In m.
  global gRoll # In radians.
  global gPitch # In radians.
  global gYaw # In radians.
  global gJawPos # In units (0 = closed, 3 = open)
  global gFeedrate # In m/s or 10rps, whichever is limitting movement.
  global gPrevFrame # In m & PyKDL.M.
  global gGcodeChanged # Is true when a new gcode file has been received.
  # Process substrings of the standardized instruction.
  move_this_call = False
  is_rapid = False
  chunks = instruction.split(' ')
  for chunk in chunks:
    if chunk == 'G00':
      is_rapid = True
      move_this_call = True
    if chunk == 'G01':
      is_rapid = False
      move_this_call = True
    if chunk[0] == 'X':
      gXPos = float(chunk[1:]) / 1000.
    if chunk[0] == 'Y':    
      gYPos = float(chunk[1:]) / 1000.
    if chunk[0] == 'Z':
      gZPos = float(chunk[1:]) / 1000.
    if chunk[0] == 'A':
      gRoll = float(chunk[1:]) * np.pi / 180
    if chunk[0] == 'B':
      gPitch = float(chunk[1:]) * np.pi / 180
    if chunk[0] == 'C':
      gYaw = float(chunk[1:]) * np.pi / 180
    if chunk[0] == 'D':
      gJawPos = float(chunk[1:])
    if chunk[0] == 'F':
      gFeedrate = float(chunk[1:]) / 1000. / 60.
    
  # Exit if no moving.
  if move_this_call == False:
    return
    
  # Control PSM.
  next_frame = kdl.Frame(gPrevFrame.M, gPrevFrame.p)
  
  # Define variables.
  temp_feedrate = gFeedrate
  if is_rapid == True:
    temp_feedrate = 100.000 # m/s
  target_rotation = kdl.Rotation.RotZ(gYaw) * kdl.Rotation.RotX(gPitch) * kdl.Rotation.RotY(gRoll) * gStartFrame.M
  prev_pos = gPrevFrame.p
  target_pos = gStartFrame.p + kdl.Vector(gXPos, gYPos, gZPos)
  prev_quat = np_quat.quaternion(gPrevFrame.M.GetQuaternion()[3], gPrevFrame.M.GetQuaternion()[0], gPrevFrame.M.GetQuaternion()[1], gPrevFrame.M.GetQuaternion()[2])
  target_quat = np_quat.quaternion(target_rotation.GetQuaternion()[3], target_rotation.GetQuaternion()[0], target_rotation.GetQuaternion()[1], target_rotation.GetQuaternion()[2])
  
  # Calculate parameters for correct speed.
  kMoveFreq = 100.
  displacement = target_pos - prev_pos
  rot_angle_axis = np_quat.as_rotation_vector(target_quat * prev_quat.conjugate())
  angular_change = np.linalg.norm(rot_angle_axis) * 180 / np.pi
  num_interpositions = np.max([displacement.Norm(), angular_change/3600]) / gFeedrate * kMoveFreq
  
  # Iterate through t values.
  for t in np.linspace(0, 1, int(num_interpositions) + 2): # +2 because this makes sure t=1 is accounted for (wont be if value < 2)
    # Lerp between previous and target positions.
    next_frame.p = prev_pos*(1-t) + (target_pos)*(t)
    # Slerp between previous and target rotations.
    next_quat = np_quat.slerp(prev_quat, target_quat, 0, 1, t)
    next_frame.M = kdl.Rotation.Quaternion(next_quat.x, next_quat.y, next_quat.z, next_quat.w)
    # Send move command.
    psm.move_cp(next_frame)
    rospy.sleep(1.0/kMoveFreq)
    # Send jaw movement command.
    jaw_pos = JointState()
    jaw_pos.position = [gJawPos]
    gJawPublisher.publish(jaw_pos)
    # Update gPrevFrame.
    gPrevFrame = copy.deepcopy(next_frame)
    # Escape if a new command has been received.
    if gGcodeChanged == True:
      return

# Main function below.
while True:
  # Wait until new Gcode is recieved
  if gGcodeChanged == False:
    continue
  gGcodeChanged = False
  
  # Start processing Gcode.
  gcode_lines = gGcodeString.split('\n')
  for line in gcode_lines:
    # Check if new Gcode was stored (currently overwrites).
    if gGcodeChanged == True:
      break
    print(line)
    ProcessInstructionStr(line)





































