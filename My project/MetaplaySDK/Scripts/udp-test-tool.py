import socket
import sys
import os
import struct
from argparse import ArgumentParser
import threading
import time
import math

def ask_raw(s, msg, ip, port):
  s.sendto(msg, (ip, port))
  s.settimeout(5)
  (reply, addr) = s.recvfrom(4096)
  return reply

def ask(msg, ip, port):
  try:
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    reply = ask_raw(s, msg.encode("utf-8"), ip, port)
    print("reply: ", reply.decode("utf-8"))
  finally:
    s.close()

def bandwidth_str(size):
  if size < 1000:
     return f"{size} B/s"
  if size < 1000_000:
     return f"{size/1000:.1f} kB/s"
  return f"{size/1000_000:.2f} MB/s"

def analyze_range(write_buf, read_buf, time_start, time_end):
  ping_in_range = {}
  for id, ts in write_buf.items():
    if ts < time_start or ts >= time_end:
      continue
    ping_in_range[id] = ts

  ping_pong_in_range = []
  for (id, send_ts) in ping_in_range.items():
    if id in read_buf:
      ping_pong_in_range.append((id, send_ts, read_buf[id], read_buf[id] - send_ts))

  num_sent = len(ping_in_range)
  num_lost = len(ping_in_range) - len(ping_pong_in_range)

  num_reorders = 0
  pong_id_order = list(read_buf.keys())
  for ndx in range(1, len(ping_pong_in_range)):
    prec_id = ping_pong_in_range[ndx - 1][0]
    succ_id = ping_pong_in_range[ndx][0]
    if pong_id_order.index(prec_id) > pong_id_order.index(succ_id):
        num_reorders += 1

  mean_ping = "n/a"
  max_ping = "n/a"
  min_ping = "n/a"
  if len(ping_pong_in_range) > 0:
    mean_ping_ms = sum(duration_ns / 1_000_000 for (_, _, _, duration_ns) in ping_pong_in_range ) / len(ping_pong_in_range)
    mean_ping = f"{mean_ping_ms:.2f}"
    max_ping_ms = max(duration_ns / 1_000_000 for (_, _, _, duration_ns) in ping_pong_in_range )
    max_ping = f"{max_ping_ms:.2f}"
    min_ping_ms = min(duration_ns / 1_000_000 for (_, _, _, duration_ns) in ping_pong_in_range )
    min_ping = f"{min_ping_ms:.2f}"

  stddev_ping = "n/a"
  if len(ping_pong_in_range) > 1:
    mean_ping_ns = sum(duration_ns for (_, _, _, duration_ns) in ping_pong_in_range ) / len(ping_pong_in_range)
    std_dev_ping_ms = math.sqrt(sum(math.pow((duration_ns - mean_ping_ns) / 1_000_000, 2) for (_, _, _, duration_ns) in ping_pong_in_range ) / len(ping_pong_in_range))
    stddev_ping = f"{std_dev_ping_ms:.1f}"

  # cleanup
  pings_to_clean = []
  for id, ts in write_buf.items():
    if ts < time_end:
      pings_to_clean.append(id)
  for id in pings_to_clean:
    del write_buf[id]

  pongs_to_clean = []
  for id, ts in read_buf.items():
    if ts < time_end:
      pongs_to_clean.append(id)
  for id in pongs_to_clean:
    del read_buf[id]

  return (num_sent, num_lost, mean_ping, min_ping, max_ping, stddev_ping, num_reorders)


def test(ip, port, packet_len, packet_rate, test_limit):
  s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
  try:
    reply = ask_raw(s, "pinghelo".encode("utf-8"), ip, port)
    if reply.decode("utf-8") != "ponghelo":
      print(f"Unexpected hello reply from UDP test server at {ip}:{port}")
      sys.exit(3)
  except socket.timeout:
    print(f"Could not connect to the UDP test server at {ip}:{port}")
    sys.exit(2)
  print(f"Connected to the UDP test server at {ip}:{port}. Performing test.")
  print(f" Packet rate: {packet_rate} packets/s")
  print(f" Packet size: {packet_len} B")
  print(f" Expected bandwidth: {bandwidth_str(packet_rate * packet_len)}")
  print()

  s.settimeout(None)

  read_buf = {}
  read_buf_lock = threading.Lock()

  def reader():
    pong_bytes = "pong".encode("utf-8")
    while True:
      (reply, addr) = s.recvfrom(4096)
      reply_at = time.perf_counter_ns()
      if len(reply) != packet_len or reply[0:4] != pong_bytes:
        print("invalid response")
      (reply_ndx, ) = struct.unpack_from("=l", reply, 4)
      read_buf_lock.acquire()
      read_buf[reply_ndx] = reply_at
      read_buf_lock.release()

  write_buf = {}
  write_buf_lock = threading.Lock()
  start_at = time.perf_counter_ns()
  start_at_wall_clock = time.time_ns()

  def writer():
    num_messages_sent = 0
    ns_per_packet = 1_000_000_000 / packet_rate
    buffer = bytearray(packet_len)
    buffer[0:4] = "ping".encode("utf-8")
    while True:
      struct.pack_into("=l", buffer, 4, num_messages_sent)
      next_send_time = start_at + num_messages_sent * ns_per_packet
      send_time = time.perf_counter_ns()
      if send_time >= next_send_time:
        s.sendto(buffer, (ip, port))
        write_buf_lock.acquire()
        write_buf[num_messages_sent] = send_time
        write_buf_lock.release()
        num_messages_sent += 1
      else:
        time.sleep(0.0001)

  reader_thread = threading.Thread(target=reader)
  reader_thread.deamon=True
  reader_thread.start()

  writer_thread = threading.Thread(target=writer)
  writer_thread.deamon=True
  writer_thread.start()

  analyzed_second = 0
  write_buf_copy = {}
  read_buf_copy = {}
  total_num_lost = 0
  total_num_sent = 0
  total_num_reorders = 0

  try:
    while True:
      time.sleep(0.1)
      current_second = (time.perf_counter_ns() - start_at) // 1_000_000_000
      complete_second = current_second - 2 # Current second is in progress, previous might complete now. 2 seconds ago is valid
      if complete_second <= analyzed_second:
        continue

      read_buf_lock.acquire()
      read_buf_copy.update(read_buf)
      read_buf = {}
      read_buf_lock.release()

      write_buf_lock.acquire()
      write_buf_copy.update(write_buf)
      write_buf = {}
      write_buf_lock.release()

      analyze_start = start_at + analyzed_second * 1_000_000_000
      analyze_end = start_at + (analyzed_second + 1) * 1_000_000_000
      (num_sent, num_lost, mean_ping, min_ping, max_ping, stddev_ping, num_reorders) = analyze_range(write_buf_copy, read_buf_copy, analyze_start, analyze_end)

      analyze_end_wall_clock = start_at_wall_clock + (analyzed_second + 1) * 1_000_000_000
      ts = time.strftime("%Y-%m-%d %H:%M:%S", time.gmtime(analyze_end_wall_clock // 1_000_000_000))
      print(f"[{ts}] ({ip}:{port}): Sent {num_sent}, Lost {num_lost}, Reorder {num_reorders}: Ping(ms) mean:{mean_ping} min:{min_ping} max:{max_ping} stddev:{stddev_ping}")

      total_num_sent += num_sent
      total_num_lost += num_lost
      total_num_reorders += num_reorders

      analyzed_second += 1
      if test_limit is not None and analyzed_second >= test_limit:
        break

  except KeyboardInterrupt:
    pass

  print(f"Total sent {total_num_sent}, total lost {total_num_lost}, total reorder {total_num_reorders}")
  os._exit(0)

def main():
  parser = ArgumentParser(description='Metaplay Debug UDP test tool.')
  parser.add_argument('host', nargs=1)
  parser.add_argument('port', nargs=1)
  parser.add_argument('-rate', default=100, help="Num packets per second")
  parser.add_argument('-size', default=100, help="Packet size")
  parser.add_argument('-count', default=None, help="For how many seconds to run")

  args = parser.parse_args()
  ipv4 = socket.gethostbyname(args.host[0])
  port = int(args.port[0])
  packet_len = int(args.size)
  packet_rate = int(args.rate)
  test_limit = None if args.count is None else int(args.count)

  test(ipv4, port, packet_len, packet_rate, test_limit)

if __name__ == '__main__':
    main()
