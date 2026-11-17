import React from 'react';
import {AbsoluteFill, Audio, Composition, Img, Sequence, interpolate, registerRoot, staticFile, useCurrentFrame} from 'remotion';
const W=1280,H=720,FPS=30, shot=180;
const clamp=x=>Math.min(1,Math.max(0,x));
const ease=x=>1-(1-clamp(x))**3;
const pink='#ff638d',blue='#72cfff';
const chapters=[
 ['NEON WAVE','دسکتاپت، روی موج موسیقی','DESKTOP PLAYER'],
 ['SPECTRUM','فرکانس‌ها زیر دست تو','LIVE VISUALIZER   /   CUSTOM EQ'],
 ['CHROMA','هر کاور، یک پالت تازه','ALBUM-DRIVEN COLOR'],
 ['AUTOMIX','از یک آهنگ به آهنگ بعد','ADAPTIVE TRANSITIONS']
];
function Scene({i}){
 const f=useCurrentFrame(),t=f/FPS,a=ease(f/29),exit=clamp((180-f)/18),accent=i===2?'#ffa488':i===1?blue:pink;
 const [title,caption,tag]=chapters[i];
 const signal=(n)=>clamp(.09+.57*(.5+.5*Math.sin(n*.18-t*4))*(.5+.5*Math.cos(n*.38+t*2))+.27*Math.exp(-4*(t*1.6%1))*(1-n/70));
 const window=<div style={{position:'relative',width:617,height:230}}><Img src={staticFile('player.png')} style={{width:'100%'}}/><div style={{position:'absolute',left:157,top:89,width:441,height:47,display:'flex',alignItems:'end',gap:2}}>{Array.from({length:64},(_,n)=><div key={n} style={{flex:1,height:signal(n)*47,background:'linear-gradient(#ff7b9c,#6ba9ff)'}}/>)}</div></div>;
 return <AbsoluteFill style={{opacity:Math.min(clamp((f+1)/13),exit),color:'#fcf4fb',fontFamily:'Segoe UI, sans-serif',overflow:'hidden'}}>
 <div style={{position:'absolute',inset:0,background:'radial-gradient(ellipse at 75% 49%, '+accent+'24, transparent 52%)'}}/>
 <svg width="1280" height="720" style={{position:'absolute',opacity:.27}}>{Array.from({length:22},(_,j)=><path key={j} d={`M -30 ${510+j*10} Q 370 ${420+j*9+12*Math.sin(t*.4)} 720 ${527+j*9} T 1320 ${507+j*10}`} fill="none" stroke={accent} strokeWidth=".8"/>)}</svg>
 <div style={{position:'absolute',left:70,right:70,top:52,borderTop:'1px solid #655469',paddingTop:15,display:'flex',justifyContent:'space-between',font:'15px Consolas',letterSpacing:3,color:'#cab9d0'}}><span>●　NEON WAVE</span><span>STEREO FM　84</span></div>
 <div style={{position:'absolute',left:70,top:230,transform:`translateY(${(1-a)*32}px)`,opacity:a}}>
 <div style={{font:'16px Consolas',letterSpacing:3,color:accent}}>{tag}</div>
 <div style={{fontWeight:900,fontSize:i===1?73:79,letterSpacing:-4,marginTop:7}}>{title}</div>
 <div dir="rtl" style={{textAlign:'left',fontSize:29,color:'#e6ccdf',marginTop:7}}>{caption}</div></div>
 <div style={{position:'absolute',left:600,top:195,width:610,height:300,perspective:1050,display:'grid',alignItems:'center',transform:`translateX(${(1-a)*65}px)`}}>
 {i===0||i===3?<div style={{transform:`rotateY(${-12+3*t}deg) rotateZ(${-3+t*.45}deg) scale(.98)`,borderRadius:10,overflow:'hidden',boxShadow:'0 22px 65px #000a,0 0 35px '+accent+'33'}}>{window}</div>:
 i===1?<div style={{height:180,display:'flex',alignItems:'end',gap:4,borderBottom:'1px solid '+blue}}>{Array.from({length:54},(_,n)=><div key={n} style={{flex:1,height:14+signal(n)*200,background:'linear-gradient(#ffaac7,#b170ba 45%,#5085bf)',borderTop:'3px solid white'}}/>)}</div>:
 <div style={{display:'flex',alignItems:'center',gap:22}}><div style={{width:230,height:230,borderRadius:12,backgroundImage:`url(${staticFile('player.png')})`,backgroundSize:'1234px 460px',backgroundPosition:'-29px -143px',boxShadow:'0 18px 50px #0008',transform:`rotate(${-5+t*.6}deg)`}}/>{['#322c40','#555568','#995562','#c84456'].map((c,n)=><div key={c} style={{background:c,width:43,height:150,borderRadius:24,transform:`translateY(${(1-ease((f-9*n)/26))*65}px)`,opacity:ease((f-9*n)/26)}}/>)}</div>}
 </div>
 {i===3&&<svg style={{position:'absolute',left:653,top:482}} width="500" height="70">{[pink,blue].map((c,n)=><path key={c} d={n?'M0 58 C250 58 245 8 500 8':'M0 8 C250 8 245 58 500 58'} pathLength="1" strokeDasharray={ease(f/85)+' 1'} stroke={c} strokeWidth="3" fill="none"/>)}</svg>}
 <div style={{position:'absolute',left:70,right:70,bottom:53,display:'flex',gap:8}}>{chapters.map((_,n)=><div key={n} style={{height:2,flex:1,background:'#40354a'}}><div style={{width:(n<i?100:n===i?100*f/180:0)+'%',height:2,background:accent}}/></div>)}</div>
 </AbsoluteFill>
}
function Film(){return <AbsoluteFill style={{background:'#100d1b'}}><Audio src={staticFile('score.wav')}/>{chapters.map((_,i)=><Sequence key={i} from={i*shot} durationInFrames={shot}><Scene i={i}/></Sequence>)}</AbsoluteFill>}
registerRoot(()=> <Composition id="NeonWave" component={Film} durationInFrames={720} fps={FPS} width={W} height={H}/>);
