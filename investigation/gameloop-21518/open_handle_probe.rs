use std::{fs::{self,File},os::unix::fs::FileExt,process::Command};
fn main() {
    let pid=std::env::args().nth(1).expect("pid");
    let maps=fs::read_to_string(format!("/proc/{pid}/maps")).expect("maps");
    let row=maps.lines().find(|s|s.ends_with("/libUE4.so")&&s.split_whitespace().nth(2)==Some("00000000")).expect("UE4 map");
    let base=u64::from_str_radix(row.split('-').next().unwrap(),16).unwrap();
    let memory=File::open(format!("/proc/{pid}/mem")).expect("mem");
    let restored=Command::new("/system/bin/sh").args(["-c","setenforce 1; getenforce"]).output().expect("restore");
    println!("restore_status={} enforcement={}",restored.status,String::from_utf8_lossy(&restored.stdout).trim());
    let mut data=[0u8;16];
    println!("base={base:x} read={:?} bytes={data:02x?}",memory.read_at(&mut data,base));
}
