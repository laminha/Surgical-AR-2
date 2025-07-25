import socket
import rospy
from std_msgs.msg import String

# ROS publisher boilerplate.
rospy.init_node("TcpServer_py");
publisher = rospy.Publisher('gcode', String, queue_size=10)

# TCP constants
kHost = '' # Listen on all interfaces.
kPort = 5005

# Main loop.
while True:
  with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
    # Say we are fine taking control of the TCP port even if its occupied (in TIME_WAIT).
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    # TCP boilerplate.
    s.bind((kHost, kPort))
    s.listen(1)
    print(f"Listening on port {kPort}...")
    conn, addr = s.accept()
    with conn:
      print('connected by', addr)
      while True:
        data = conn.recv(1024)
        if not data:
          break
        data_str = data.decode('utf-8').strip()
        print(data_str)
        
        # Main logic.
        publisher.publish(data_str)
        
  # Unity app has closed, psuedo clear screen and safety sleep before next main loop.
  print('\n\n\n\n\n\n\n')
  rospy.sleep(5)
