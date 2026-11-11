from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'native/reader.rs').read_text().split('\nfn main() {')[0]
extra=r'''
fn samples(r:&mut Reader)->Result<String> {
    let engine=r.memory.ptr(r.base+0xf621d90)?;let contexts=r.memory.ptr(engine+0xc40)?;let count=r.memory.int(engine+0xc48)?;
    if !(1..=32).contains(&count){return Err("contexts".into());}
    let mut world=0;for i in 0..count {let c=r.memory.ptr(contexts+i as u64*8)?;if r.memory.read(c,1)?[0]==1 {world=r.memory.ptr(c+0x2a0)?;break;}}
    let level=r.memory.ptr(world+0x30)?;let header=r.memory.read(level+0xa0,16)?;let array=u64_at(&header,0);let n=i32_at(&header,8);
    if !(1..=16384).contains(&n){return Err("actors".into());}
    let entries=r.memory.read(array,n as usize*8)?;let mut result=Vec::new();
    for slot in entries.chunks_exact(8) {
        let actor=u64_at(slot,0);if actor<4096{continue;}
        let row=(||->Result<Option<String>> {
            let class=r.memory.ptr(actor+0x10)?;let info=r.class_info(class)?;if !info.player{return Ok(None);}
            let health=r.memory.float(actor+0xe60)?;if !health.is_finite()||health<=0.0{return Ok(None);}
            let mesh=r.memory.ptr(actor+0x510)?;let times=r.memory.read(mesh+0x480,20)?;
            let name=r.object_name(actor)?;let root=r.memory.ptr(actor+0x208)?;let position=r.memory.vec(root+0x220)?;
            let flags=r.memory.read(mesh+0x13a0,1).map(|b|b[0]).unwrap_or(255);
            Ok(Some(format!("{{\"Address\":\"0x{actor:x}\",\"Name\":{},\"Class\":{},\"Mesh\":\"0x{mesh:x}\",\"Position\":{},\"Time480\":[{}],\"Rendered13a0\":{flags}}}",quoted(&name),quoted(&info.name),position.json(),times.chunks_exact(4).map(|b|number(f32_at(b,0))).collect::<Vec<_>>().join(","))))
        })();
        if let Ok(Some(row))=row {result.push(row);}
    }
    Ok(format!("\"WorldTime\":{},\"Samples\":[{}]",number(r.memory.float(world+0xcc8)?),result.join(",")))
}
fn main() {
    let args:Vec<String>=env::args().collect();let pid=args[1].parse().unwrap();let base=u64::from_str_radix(&args[2],16).unwrap();
    let result=(||->Result<()>{
        let mut r=Reader::new(pid,base,"21518".into())?;let clock=Instant::now();
        for sequence in 0..60 {let started=Instant::now();let snapshot=r.snapshot(false,false,true)?;let rows=samples(&mut r)?;
            println!("{{\"Sequence\":{sequence},\"Elapsed\":{},\"Snapshot\":{snapshot},{rows}}}",clock.elapsed().as_secs_f64());
            if let Some(delay)=Duration::from_millis(100).checked_sub(started.elapsed()){thread::sleep(delay);}
        }
        Ok(())
    })();
    if let Err(error)=result {println!("{{\"Error\":{}}}",quoted(&error));process::exit(1);}
}
'''
(Path(__file__).parent/'visibility-samples.rs').write_text(source+extra)
